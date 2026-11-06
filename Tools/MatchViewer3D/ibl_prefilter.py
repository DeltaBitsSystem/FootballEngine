"""Offline split-sum GGX and cosine convolution in the runtime's Y-up space."""
import numpy as np


def directions(width, height):
    u,v=np.meshgrid((np.arange(width)+.5)/width,(np.arange(height)+.5)/height)
    phi,theta=(u-.5)*2*np.pi,v*np.pi
    return np.stack([np.sin(theta)*np.cos(phi),np.cos(theta),np.sin(theta)*np.sin(phi)],axis=-1)


def sample_latlong(direction, image):
    height,width=image.shape[:2]
    u=(np.arctan2(direction[...,2],direction[...,0])/(2*np.pi)+.5)*width-.5
    v=np.arccos(np.clip(direction[...,1],-1,1))/np.pi*height-.5
    x0,y0=np.floor(u).astype(int),np.floor(v).astype(int)
    fx,fy=(u-x0)[...,None],(v-y0)[...,None]
    x1,y1=(x0+1)%width,np.clip(y0+1,0,height-1)
    x0,y0=x0%width,np.clip(y0,0,height-1)
    return (image[y0,x0]*(1-fx)+image[y0,x1]*fx)*(1-fy)+(image[y1,x0]*(1-fx)+image[y1,x1]*fx)*fy


def radical_inverse(value):
    result,weight=0.0,.5
    while value:
        result+=(value&1)*weight
        value>>=1
        weight*=.5
    return result


def half_vector(x,y,roughness):
    alpha2=roughness**4
    cosine=np.sqrt((1-y)/(1+(alpha2-1)*y))
    sine=np.sqrt(np.maximum(0,1-cosine*cosine))
    return np.stack([np.cos(2*np.pi*x)*sine,np.sin(2*np.pi*x)*sine,cosine],axis=-1)


def prefilter(image, width=128, height=64, samples=256):
    normal=directions(width,height)
    up=np.where(np.abs(normal[...,1:2])<.99,[0,1,0],[1,0,0])
    tangent=np.cross(up,normal)
    tangent/=np.linalg.norm(tangent,axis=-1,keepdims=True)
    bitangent=np.cross(normal,tangent)

    def world(local):
        return tangent*local[0]+bitangent*local[1]+normal*local[2]

    layers=[]
    for roughness in np.linspace(0,1,6):
        if roughness==0:
            layers.append(sample_latlong(normal,image))
            continue
        total,weight=np.zeros_like(normal),np.zeros((*normal.shape[:2],1))
        for i in range(samples):
            h=world(half_vector(i/samples,radical_inverse(i),roughness))
            light=2*np.sum(normal*h,axis=-1,keepdims=True)*h-normal
            nl=np.maximum(0,np.sum(normal*light,axis=-1,keepdims=True))
            total+=sample_latlong(light,image)*nl
            weight+=nl
        layers.append(total/np.maximum(weight,1e-8))
    # Store irradiance/pi: the Lambertian shader must not divide this layer again.
    diffuse=np.zeros_like(normal)
    for i in range(samples):
        x,y=i/samples,radical_inverse(i)
        local=[np.sqrt(y)*np.cos(2*np.pi*x),np.sqrt(y)*np.sin(2*np.pi*x),np.sqrt(1-y)]
        diffuse+=sample_latlong(world(local),image)/samples
    layers.append(diffuse)
    return np.stack(layers)


def brdf_lut(size=128,samples=256):
    nv,roughness=np.meshgrid((np.arange(size)+.5)/size,(np.arange(size)+.5)/size)
    view=np.stack([np.sqrt(1-nv*nv),np.zeros_like(nv),nv],axis=-1)
    brdf=np.zeros((size,size,2))
    for i in range(samples):
        h=half_vector(i/samples,radical_inverse(i),roughness)
        vh=np.maximum(0,np.sum(view*h,axis=-1))
        light=2*vh[...,None]*h-view
        nl,nh=np.maximum(0,light[...,2]),np.maximum(1e-8,h[...,2])
        # Smith's IBL geometry remapping differs from the direct-light term.
        k=roughness**2/2
        geometry=nv/(nv*(1-k)+k)*nl/np.maximum(nl*(1-k)+k,1e-8)
        visibility=geometry*vh/np.maximum(nh*nv,1e-8)
        fresnel=(1-vh)**5
        brdf[...,0]+=(1-fresnel)*visibility/samples
        brdf[...,1]+=fresnel*visibility/samples
    return brdf

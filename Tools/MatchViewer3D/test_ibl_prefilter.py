import unittest
import numpy as np
from ibl_prefilter import brdf_lut,directions,prefilter,sample_latlong


class IblPrefilterTests(unittest.TestCase):
    def test_pixel_centres_roundtrip_without_rotation_or_vertical_flip(self):
        image=np.arange(8*4*3,dtype=float).reshape((4,8,3))
        np.testing.assert_allclose(sample_latlong(directions(8,4),image),image,atol=1e-12)

    def test_longitude_wrap_is_continuous(self):
        rays=directions(32,16)
        image=rays+1
        a=sample_latlong(np.array([-1.,0.,1e-9]),image)
        b=sample_latlong(np.array([-1.,0.,-1e-9]),image)
        np.testing.assert_allclose(a,b,atol=1e-8)

    def test_constant_radiance_survives_all_layers_without_extra_pi(self):
        color=np.array([.2,.7,1.5])
        layers=prefilter(np.broadcast_to(color,(16,32,3)),width=8,height=4,samples=64)
        np.testing.assert_allclose(layers,np.broadcast_to(color,layers.shape),atol=1e-12)

    def test_linear_sky_cosine_convolution_matches_analytic_irradiance(self):
        rays=directions(128,64)
        image=np.repeat((1+.5*rays[...,1:2]),3,axis=-1)
        layers=prefilter(image,width=8,height=4,samples=512)
        expected=np.repeat((1+directions(8,4)[...,1:2]/3),3,axis=-1)
        np.testing.assert_allclose(layers[-1],expected,atol=.01)

    def test_brdf_is_finite_positive_and_not_an_ldr_texture(self):
        lut=brdf_lut(size=16,samples=128)
        self.assertEqual(lut.shape,(16,16,2))
        self.assertTrue(np.isfinite(lut).all())
        self.assertGreaterEqual(float(lut.min()),0)
        self.assertGreater(float(lut[...,0].mean()),.1)


if __name__=='__main__':
    unittest.main()

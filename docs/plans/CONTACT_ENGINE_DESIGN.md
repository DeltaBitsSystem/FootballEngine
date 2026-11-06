# Diseño normativo: contacto causal

## 1. Diagnóstico y componentes a reutilizar

`Players/Steering.fs` integra posiciones a cadencia de steering y repele compañeros, leyendo estado parcialmente actualizado. `Players/BodyPhysics.fs` solo integra salto. `Players/Actions/Duel.fs` usa alcance de pie, sorteo de éxito y probabilidad fija de falta, transfiriendo control al ganar. `Ball/Contact.fs` selecciona receptores por radio. `Ball/FootContact.fs` ya prepara/cancela golpes y publica evidencia; `Ball/Possession.fs` ya conduce con toques finitos. Reutilizar el journal de movimiento, observación causal, unidades, RNG, configuración, participación, morfología física y frontera MatchSession. No crear un segundo mundo físico en Client.

## 2. Estado y propiedad

Añadir contratos equivalentes a los siguientes (nombres finales adaptables al estilo existente; significado obligatorio):

- `ContactId`: identificador monotónico por partido, guardado en checkpoint, sin hashes de posición ni IDs de slot como identidad persistente.
- `BodyContactState` por participante/generación: apoyo izquierdo/derecho/doble/ninguno como proxy físico Core, estabilidad normalizada, perturbación lineal/angular, estado Stable/Staggering/Falling/Recovering y fechas de entrada/salida. La pose visual no determina estos datos.
- `ContactAction`: identidad de acción, actor/generación, intención Contain/Shield/ShoulderChallenge/StandingTackle/SlidingTackle/Trap/Strike/Header, objetivo opcional, fase Preparing/Committed/Recovering/Completed/Cancelled, tick inicial/compromiso/fin, pie o superficie, trayectoria de efector prevista y error de ejecución comprometido. No sustituye los planes colectivos ni duplica PendingActions.
- `ContactRecord`: participantes/generaciones, acción vinculada opcional, tick y fracción de impacto, punto, normal, superficies, velocidades relativas, impulso normal/tangencial, evidencia de apoyo antes/después, tocó pelota y orden temporal, estado de control antes/después. Evidencia física y evaluación arbitral son registros distintos.
- Scratch físico preasignado: roots previos/predichos, candidatos, manifolds, contactos de pelota por segmento. No serializar scratch reconstruible; sí cualquier warm-start/caché que afecte próximos resultados, o no utilizar warm-start.

Propiedad: controller de movimiento/contacto modifica cinemática, equilibrio y fases de contacto; integración única modifica la pelota; EventApply mantiene autoridad sobre posesión deportiva, disciplina, participación y reinicios. La frontera consume eventos pendientes una sola vez. Dentro del tick un contacto aplicado debe invalidar inmediatamente candidatos/pending strikes incompatibles para que el retraso del reducer no permita dos acciones sobre el mismo balón. Usar marcas de resolución/versión física en scratch y metadatos de balón, no aplicar dos veces el impulso cuando se drena el evento.

Morfología: reutilizar `PhysicalMorphology`; resolver altura/masa/pie desde datos reales disponibles y configurar fallback explícito si faltan. No inferir masa a partir de Strength ni confundir morfología estándar del renderer con la del jugador. Dimensiones de proxy derivadas de esa morfología, con coeficientes configurables.

## 3. Orden temporal único

Conservar frontera de entrada de Stepper, drenaje y administración. Sustituir el orden relevante de `SystemsTable.fs`, documentando todas las nuevas visibilidades:

1. Percepción/intención/steering cuando los triggers existentes corresponden. Steering escribe objetivos de velocidad/aceleración/giro, nunca integra posiciones ni añade repulsión después de limitar aceleración. Reinicios actualizan objetivos respetando autorización y ejecutor.
2. Consumir propuestas de acción admisibles y preparar compromisos. Decisiones nuevas posteriores pueden prepararse para el próximo tick; ningún contacto retroactivo.
3. Un `ContactDynamics` a cada subtick permitido toma snapshot común de ambos equipos, avanza apoyo/salto, integra cuerpos y pelota y resuelve impactos en tiempo físico. Es el único orquestador de integración; puede reutilizar módulos puros de BallDynamics/BodyPhysics. No ejecutar además los antiguos integradores.
4. Interpretar contactos: control/posesión, lanzamiento, desvío, pérdida, desequilibrio. Resolver cruce de límites/gol de cada segmento después de cualquier contacto anterior.
5. Sistemas tácticos/cognitivos restantes según contratos de observación; arbitraje consume incidentes confirmados mediante el mecanismo de eventos, sin leer futuro; clock/period boundaries conservan su autoridad.

Definir pruebas de orden al cambiar scheduler. El steering puede mantener cadencia 20 Hz pero la integración usa `dt`, no `dtPlayer`; controles retenidos entre actualizaciones. Nadie integra 50 ms al ejecutarse por un evento adicional. Las reacciones se basan en evidencia disponible, no en conocimiento futuro de una entrada.

## 4. Geometría y solver

### Cuerpos

Proxy principal cápsula vertical de torso/pelvis, ajustada a altura, ancho y PosZ. Volúmenes separados simples de cabeza y piernas/pies para pelota y para identificar impactos peligrosos; no sumar impulsos duplicados de proxies superpuestos del mismo par. Brazos/manos son proxies limitados para evidencia accidental, no efectores arbitrarios capaces de robar.

Para 22 jugadores, escaneo de todos los pares activos (231) es una broad phase válida y acotada; filtrar por AABB barrida y altura. Preferirlo a una estructura espacial compleja sin medición. Scratch dimensionado por capacidad máxima real de participantes activos, validado al crear sesión. Identidad por jugador/generación, no posición del array.

### Detección continua

Calcular TOI barrido sobre movimiento relativo para cápsulas/esferas y trayectorias de efectores. Aproximar trayectoria curvada por segmentos con error geométrico acotado configurable; comprobar deslizamientos/saltos. Dentro del subtick avanzar al impacto más temprano, resolver, reconstruir candidatos afectados y avanzar resto. Orden estable por tiempo y claves canónicas de identidad/superficie, agrupando impactos simultáneos dentro de tolerancia; no dar prioridad Home ni al portador.

Solver secuencial de impulsos con iteraciones fijas configuradas. Para velocidad relativa normal entrante `vn < 0`, impulso `j = -(1+e)*vn/(invMassA+invMassB)`, con masa efectiva/apoyo explícitos; fricción tangencial limitada por `mu*j`. Contactos pasivos no crean energía. Los controles activos solo agregan trabajo dentro de aceleración/esfuerzo configurados. No simular al atleta como cuerpo rígido libre: apoyo permite resistencia acotada, no masa infinita ni rebote elástico humano. Separar estabilización posicional limitada de velocidad para no generar energía por corregir penetración. Normal estable y antisimétrica para centros coincidentes; comprobar simetría al intercambiar identidades.

Resolver múltiples contactos como conjunto; contactos persistentes no repiten faltas, animaciones ni impulsos de impacto cada tick. Mantener episodio por par/superficie/acción con inicio/continuación/fin y umbral físico de nuevo impacto; estado relevante serializable y acotado. Evitar separación global que impida carga legal o disputa.

Un límite de iteraciones/impactos es protección de seguridad, no permiso de atravesamiento: al agotarlo, restringir conservadoramente movimiento restante de los cuerpos implicados, registrar saturación y conservar finitud/no penetración dentro de tolerancia. Saturación recurrente en corpus es fallo de aceptación, no funcionamiento normal. No saltar contactos ni teletransportar pelota para recuperar rendimiento.

## 5. Equilibrio y comportamiento

Estabilidad depende de proxy de base de apoyo, orientación respecto del impacto, velocidad/giro/aceleración, fase de paso Core, vuelo, fatiga y atributos existentes pertinentes. Acumular perturbación según impulso y brazo respecto de centro de masa; amortiguar/recuperar con tasas físicas configuradas. Umbrales con histéresis evitan alternancia de estados. Caer no implica falta ni perder necesariamente la pelota. No caída cosmética desconectada del desplazamiento real.

Stable permite esfuerzo normal; Staggering limita giro/aceleración y precisión; Falling impide acciones incompatibles y describe desplazamiento/apoyo; Recovering recupera gradualmente. No congelar todos los jugadores tras cualquier roce. Contacto aéreo no recibe apoyo ficticio del suelo. Recuperación aterriza sin doble evento; nueva lesión/expulsión/sustitución cancela acciones de esa generación.

Contain conserva posición y evita comprometer una pierna; Shield interpone cuerpo legal y mantiene pelota en zona accesible, no crea un campo de fuerza; ShoulderChallenge busca carga lateral disputando pelota; tackle predice ventana y compromete trayectoria, no elige ganador. Agentes deciden desde percepción propia existente. La geometría local autoritativa valida ejecución; no se usa para dar información táctica omnisciente.

## 6. Ejecución de entrada y contacto con pelota

Preparación/compromiso/recuperación tienen duración y límites configurados. Antes del compromiso puede cancelar/reorientar dentro de límites; después no puede retargetear instantáneamente. Muestrear error con RNG deportivo una vez al comprometer, con identidad de acción, y guardarlo. Ningún RNG en solver ni captura; nada de resorteo por frame/render.

Usar efectores físicos Core simplificados con origen, velocidad y alcance de pierna consistente con morfología/apoyo. El radio actual puede ser broad phase, no prueba final de contacto. Pie elegido por geometría y preferencia, sin ignorar pie de apoyo. Prohibir piernas que alcancen a través de torso/rival sin colisión correspondiente.

Todos los modos de pelota pasan por una ruta única de contactos: pase, tiro, centro, despeje, libre, conducción y recepción. Un lanzamiento objetivo es una solicitud de velocidad/spin; el efector debe alcanzar la superficie de la pelota en el instante comprometido y producir impulso finito compatible con límites de ejecución. No recalcular dirección ideal al último instante sin límite de reorientación.

Recepción resuelve amortiguación normal y fricción tangencial respecto del efector; técnica/orientación/velocidad entrante/apoyo/fatiga condicionan objetivo y precisión. Fallo deja velocidad residual/rebote real, no solo BallLoose con trayectoria intacta arbitraria. Conducción programa toques alcanzables y deja física libre entre ellos. Contactos pasivos con torso/piernas/cabeza desvían según material/superficie; no otorgan posesión automática. Cabeceo intencional usa efector temporal; rebote accidental de cabeza no requiere intención.

Un tackle puede fallar, desviar, bloquear, tocar antes que el rival, producir balón dividido o robar con control. Posesión requiere ventana de control físicamente viable posterior, no ganar una comparación ni simplemente tocar. Disputas simultáneas resuelven orden físico sin preferencia sistemática; pueden quedar sin dueño. Reemplazar ambos sorteos `DuelFoulChance` y `ContestFoulChance` y la victoria binaria del duelo, no envolverlos detrás de nuevas clases.

Portero: contacto de pie nunca crea GKHoldSince. Captura con manos solo por acción/parada válida, zona legal y restricciones de cesión/reinicio existentes; no cambiar pie por mano a mitad de una disputa sin preparación. Mantener semántica del agarre explícito. Manos no autorizadas producen evidencia para regla, no control legal automático.

## 7. Arbitraje

Crear incidente desde acción/contacto con identidad estable, infractor/víctima explícitos, zona, intensidad, dirección, intento legítimo, riesgo, secuencia pelota/cuerpo y efecto sobre libertad/equilibrio. No todo choque es falta y no toda falta necesita impacto (juego peligroso u obstrucción). Bola primero no exime fuerza excesiva. Falta ofensiva y defensiva se evalúan por conducta, no invirtiendo siempre los argumentos actuales.

Clasificar legal/careless/reckless/excessive con umbrales justificables y configurados. Separar clasificación física de SPA/DOGSO: distancia al arco atacado por la víctima, dirección de juego, probabilidad real de controlar pelota y defensores capaces de intervenir, no mero último hombre. Aplicar distinciones reglamentarias del penal/intento de jugar o disputar pelota. Validar contra reglas oficiales actuales y documentar artículos/fuentes; no inventar porcentajes de tarjeta. No implementar sesgos arbitrales nuevos como sustituto de evidencia.

Arbitraje conserva una única respuesta incidente→ventaja/reinicio+disciplina. Ventaja evalúa beneficio real y conserva incidente para volver a la falta si no se materializa dentro de ventana; sanción disciplinaria diferida se aplica una vez en el momento permitido. Auditar el área usando `Pitch.isInPenaltyArea` (parámetro attackingSide) y lado de víctima. Ubicación del contacto infractor, no centro del portador, define reinicio. Reinicios deduplican incidentes y no generan faltas repetidas por cuerpos ya trabados. Mantener perímetro físico durante dead-ball sin permitir disputas deportivas antes de autorización.

## 8. Evidencia y presentación

Registrar preparación, contacto confirmado/cancelado y consecuencia enlazados por ActionId/ContactId/ObservationId. Incluir fracción temporal, normal, punto, actor y otro participante, superficie, impulso, cambio de equilibrio y causa. Ampliar tipadamente MotionEvidence o añadir payload tipado integrado; no reutilizar BallCenter como posición de otro jugador ni codificar varios significados en un vector. Journal deportivo requerido independiente de captura diagnóstica opcional; capacidad/pérdida explícitas.

Client consume evidencia respetando AvailableAt y generaciones, usa raíces físicas, apoyo procedural e IK existentes, y representa carga, bloqueo, entrada, tropiezo/caída/levantada. No empuja roots de autoridad por animación ni decide si hubo toque por intersección del rig. No importar clips ajenos ni crear assets en esta unidad. Si el rig no permite un gesto, adaptar pose procedural y demostrar límites visuales sin omitir consecuencia deportiva.

## 9. Contratos incompatibles y seguridad

Retirar campos/rutas obsoletos y actualizar consumidores en una sola versión coherente. Incrementar esquemas de checkpoint/archive/replay/save/config afectados; rechazar versiones anteriores con Result y mensaje claro antes de decodificar estado parcial. Nunca borrarlas ni migrarlas silenciosamente. Export/import y reinicio de sesión preservan IDs, acciones, equilibrio, episodios, RNG y cursor/journal necesario. Slots sustituidos no heredan contacto del jugador anterior. Ninguna dependencia de wall-clock, red o archivos durante tick. No prometer igualdad bit a bit entre arquitecturas no probadas.

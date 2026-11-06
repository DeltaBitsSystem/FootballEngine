# Contacto causal del match engine

## Steps
- [ ] Validar y registrar el baseline compartido sin descartar cambios existentes.
- [ ] Sustituir contratos de movimiento/contacto y conectar checkpoint, configuración y observación.
- [ ] Implementar integración corporal y contactos continuos deterministas.
- [ ] Implementar equilibrio, protección y disputas temporales de pelota.
- [ ] Integrar contactos de pelota, posesión, reglas y consecuencias deportivas.
- [ ] Integrar presentación procedural y replay sin autoridad deportiva en Client.
- [ ] Superar pruebas causales, regresiones, mediciones y revisión de entrega.

## Objetivo y autoridad

Implementar de extremo a extremo contacto jugador–jugador y jugador–pelota causal, determinista y visible, no únicamente una corrección cosmética de separación. El posicionamiento colectivo y las decisiones individuales crean las condiciones; geometría, ejecución, apoyo y momento determinan el contacto; las consecuencias deportivas y arbitrales interpretan evidencia. Mantener realismo y diversión sin fabricar resultados.

Solicitud del usuario: diseñar e implementar todo en una sesión GPT-6 Luna Max; sin backward compatibility. Estos pasos son hitos internos, no solicitudes de aprobación sucesivas. El implementador debe completar todos o demostrar un bloqueo real. No autoriza commits, reset, eliminación de datos, worktree nuevo ni modificación de assets Blender. Trabajar en el checkout actual `rewrite-match-engine`, preservando su baseline no comprometido. No crear agentes adicionales.

Documentos normativos de esta unidad:
- `docs/plans/CONTACT_ENGINE_DESIGN.md`: arquitectura, algoritmos y contratos.
- `docs/plans/CONTACT_ENGINE_INTEGRATION.md`: propietarios y consumidores.
- `docs/plans/CONTACT_ENGINE_ACCEPTANCE.md`: pruebas, rendimiento y evidencia.

Leer además `AGENTS.md`, `docs/evidence/MATCH_ENGINE_FOUNDATION.md`, `docs/evidence/PLAYER_REACTION.md`, `docs/evidence/PLAYER_MOTION_DESIGN.md` y los contratos actuales. El código local, incluidos archivos no seguidos, es el baseline. Si hay contradicción material con este diseño, registrar evidencia y pedir corrección; no inventar otra arquitectura ni recortar alcance.

## Alcance cerrado

Incluye cuerpos de ambos equipos; compañeros, adversarios y porteros; contacto terrestre/aéreo; protección, carga, contención y entrada de pie/deslizante; bloqueo/desvío accidental de pelota con cuerpo, pies y cabeza; recepción y conducción; equilibrio y recuperación; falta ofensiva/defensiva, ventaja y disciplina; evidencia, replay y checkpoints; reacción visual procedural. Los contactos de manos accidentales requieren evidencia/regla, sin otorgar a jugadores de campo capacidad de atrapar. Preservar restricciones de portero y reinicios.

No incluye ragdoll general, biomecánica médica, autoría Blender, nuevos modelos/texturas, rediseño de inteligencia colectiva, rediseño del sistema completo de tiros/paradas, cambios de UX ajenos ni certificación de superioridad a Football Manager. Conectar los tiros/paradas existentes al contacto para impedir que ignoren una intercepción física previa. Eliminar bypasses incompatibles dentro del alcance; conservar funcionalidades válidas.

## Filosofía aplicada

- No victoria de entrada sin contacto alcanzable; no posesión por proximidad ni tarjeta sorteada por coordenadas.
- Atributos alteran percepción, precisión, esfuerzo y recuperación, no leyes físicas ni autorización geométrica.
- La incertidumbre reside en ejecución comprometida, no en tirar otra moneda después del contacto.
- Separar evitar, contactar, desequilibrar, controlar e infringir: ninguna equivalencia automática.
- Física a 40 Hz; agentes por relevancia/cadencia existente; render sin influencia deportiva.
- Todos los parámetros de comportamiento en configuración validada. Tolerancias numéricas explícitas y documentadas no son ajustes de balance escondidos.

## Entrega

Actualizar únicamente las casillas al terminar hitos; no borrar restricciones. Crear `docs/evidence/CONTACT_ENGINE.md` con implementación real, archivos, matriz de aceptación, comandos/resultados, mediciones reproducibles y límites honestos. Añadir scripts/fixtures deterministas reutilizables para probes. No llamar completo a un build exitoso ni a un documento de intención. La revisión independiente del agente principal sigue siendo necesaria después de la entrega del implementador.

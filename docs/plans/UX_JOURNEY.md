# Experiencia de carrera — diagnóstico, implementación y validación

## Objetivo

Profundidad en las decisiones y sencillez en la interacción. Ninguna modificación del motor deportivo. Preservar el trabajo de rendimiento que ya estaba sin commit al iniciar esta tarea.

## Evidencia inicial (18 octubre 2026)

Build baseline de solución, Client.Tests y Benchmarks Release: correcto. Lectura de AGENTS.md y registro de git status: completos. Se recorrieron Route, AppState, UpdateSim, InboxSlice, TacticsSlice, Home, Header, Tactic, Inbox, setup y la conexión con MatchRuntime.

Capturas deterministas reales: `/tmp/opencode/ux-before` (setup, home, squad, tactics, inbox, calendar, match). Se inspeccionaron Home, Tactics e Inbox. La captura Avalonia no incluye la superficie GPU nativa: no usarla como prueba visual completa del partido.

También se abrió la aplicación normal con `XDG_DATA_HOME=/tmp/opencode/ux-play-data`, se creó una carrera mediante clics (país → manager/nombre/estilo → club → opciones → confirmación), se llegó a Hoy y se cerró la ventana. No se tocaron los saves reales del usuario. Capturas del recorrido: `/tmp/opencode/ux-live-*.png`.

Observaciones:
- Hoy priorizaba un banner, indicadores y tablas sin una recomendación de siguiente paso ni disponibilidad del once.
- La carrera creada empieza el 1 de julio con próximo partido el 1 de agosto; la acción principal solo avanza un día. Llegar al partido sin otras acciones requiere hasta 31 avances manuales (deducido de fechas/código, no un tiempo humano medido).
- El encabezado prometía ir al partido, pero despachaba siempre Advance 1, incluso con partido pendiente hoy.
- AdvanceDone enviaba navegación prematch e inicio automático; no había una preparación real con confirmación del usuario.
- Los filtros de bandeja ya existían en el estado/query, pero InboxPresenter ignoraba ese query. «Mark as Done» solo marca ActionTaken y no ejecuta la decisión de dominio.
- MatchViewer requiere RuntimeId para mostrar su contenido. Al terminar, el runtime se libera y PendingResult necesita confirmación; hace falta una vista de resultado que no dependa del renderizador activo.
- Táctica tiene Undo, Quick Pick y tradeoffs, pero el flujo es denso, mezcla idiomas y depende demasiado de drag-and-drop. Sus cambios editan el once actual; no hay un contrato separado de confirmar borrador.
- Salir de la ruta de partido cierra el runtime y limpia la vista. Revisar navegación/resume para evitar perder el contexto o un resultado pendiente.
- El guardado manual tiene estado/resultado; varios autosaves son fire-and-forget. No prometer feedback de todos los guardados sin conectar sus confirmaciones.

## Implementado hasta ahora

- CareerFlow: selector compartido de continuación, disponibilidad real de titulares, último resultado y detección de nuevas decisiones/bajas.
- ContinueCareer y AdvanceToNextEvent: avance diario explícito hacia el próximo partido, pausa por nuevas decisiones/bajas y cancelación al finalizar el día. Un Advance de varios días se limita antes del partido del usuario.
- Un partido pendiente hoy abre preparación sin avanzar la fecha ni iniciar simulación. AdvanceDone ya no inicia automáticamente el partido.
- CareerJourney: orientación accionable en Hoy, preparación con validación y confirmación explícita, revisión/registro de resultado sin requerir renderer activo.
- Header y versión compacta usan la continuación compartida; Hoy enlaza directamente al once, mensajes pendientes y último resultado.
- Bandeja utiliza el query de filtros existente, añade Todos/Por revisar/No leídos y aclara que marcar revisado no ejecuta decisiones.
- Textos nuevos en catálogo español/inglés.

## Validación incremental realizada

- Builds Client y Client.Tests Release correctos tras conectar el selector.
- 24 tests LiveMatch aprobados, incluidos tres nuevos sobre preparación sin simulación, respeto a decisiones pendientes y detección de atención nueva.
- Build Client correcto tras conectar vistas de carrera y filtros.
- Capturas reales de primera pasada: `/tmp/opencode/ux-first-pass`. Home inspeccionado: acciones visibles, disponibilidad 11/11 y preparación en encabezado; se corrigió un texto singular/plural y la ayuda contextual posterior a esa captura.

## Pendiente antes de cerrar la entrega

### Actualización de implementación y evidencia

- Navegar fuera del partido conserva runtime y resultado pendiente; pausa al salir y bloquea abrir otro partido que sustituya el activo. Comprobado en aplicación aislada: regreso al mismo partido pausado en 03:49. Capturas registradas durante la sesión: `ux-native-away-live.png` y `ux-native-return-paused.png` en `/tmp/opencode` (artefactos temporales, no evidencia durable versionada).
- Selección de puesto por botón y candidato mediante ComboBox: alternativa por clic/teclado al arrastre, comparación de afinidad y estado, intercambio entre titulares sin perder al desplazado y deshacer. Guardado, cierre y carga reales conservaron nuevo arquero y fecha en la carrera aislada.
- La prueba real descubrió que el once sugerido incluía un jugador no disponible. Corregido el filtro y añadida regresión para suspendidos. Preparación pasó de bloquear inicio por Ian Torres a mostrar once disponible tras sugerencia explícita del usuario.
- Bandeja: filas seleccionables por teclado y destinos contextuales; marcar revisado sigue siendo un acuse, no una decisión de dominio.
- Guardado manual bloqueado durante runtime/resultado pendiente, con explicación; cierre ofrece conservar sesión o salir explícitamente perdiendo el partido, sin borrar el último save. Este diálogo nuevo todavía requiere validación visual.
- Controles del partido: volver a Hoy ya no despacha cierre destructivo. Añadido feedback de orden enviada/aceptada/aplicada/rechazada y acceso denominado Evidencia; aplicar una orden no se presenta como prueba de eficacia táctica. Pendiente inspeccionar estos últimos cambios en pantalla.
- Avance fallido informa y permite reintentar; cambios diarios invalidan el indicador de guardado anterior. Pendiente prueba de fallo inyectado y cancelación en interfaz.
- Última verificación: build Release de Client.Tests correcto; suite completa **169/169** aprobada; `git diff --check` correcto. Logs: `/tmp/opencode/ux-latest-build.log` y `/tmp/opencode/ux-latest-tests.log`. No equivale a comprobar el recorrido UI completo.
- La aplicación de prueba fue detenida por el usuario. No se reinició automáticamente ni se atribuye esa detención a un fallo. El partido real no llegó a término en esta validación; el registro idempotente de un resultado real simulado sí está cubierto por test.

### Prioridad restante

Validación adicional (19 octubre): solución Release y Benchmarks compilan; **627/627 Core tests** aprobados (`ux-final-solution.log`, `ux-final-benchmark-build.log`, `ux-final-core.log` en `/tmp/opencode`). Capturas deterministas de 1100×800 regeneradas en `/tmp/opencode/ux-verified-final`; inspeccionados Táctica y Bandeja. Se detectó solapamiento del distintivo Por revisar con fecha y se corrigió apilando asunto/distintivo. Build Client y nueva captura `/tmp/opencode/ux-inbox-layout/00_Inbox (InboxRoute None).png` inspeccionada: sin ese solapamiento. Permanece mezcla de idioma en textos heredados. Estas comprobaciones no validan todavía el final del partido real.

P0: completar partido real → registro → siguiente jornada → guardar/cargar; verificar diálogo de salida, feedback de órdenes y retorno por el botón del partido. P1: cancelar avance, inyectar errores, revisar filtros vacíos y repetir capturas finales en ambos tamaños e idiomas. P1: repetir builds de solución/Benchmarks y suite Core sobre el estado final. P2: comparativa de tareas con pasos/retrocesos/errores y pruebas con jugadores externos; la reducción de clics no acredita comprensión. Mantener como limitaciones el análisis causal postpartido incompleto, la localización heredada mixta y la selección de sustituciones del visor aún limitada.

1. Verificar navegación de un runtime activo/resultado pendiente y proteger el contexto al volver a Hoy/táctica.
2. Mejorar preparación/táctica: feedback de cambios, retorno claro a preparación y alternativa accesible a drag-only, sin decisiones automáticas.
3. Completar bandeja: destinos contextuales reales, selección accesible por teclado y estado vacío filtrado preciso.
4. Dar feedback explícito de interrupción del avance, errores recuperables y guardado; probar cancelación, nuevas bajas y llegada al partido.
5. Recorrer preparación → iniciar → gestionar → terminar → registrar → próximo partido en aplicación real, con capturas y pruebas de estado. Comprobar métricas/eventos sin inventar causalidad.
6. Repetir tareas antes/después: detectar atención, preparar once, cambiar táctica, avanzar al momento relevante, interpretar resultado, guardar/retomar. Registrar acciones y límites; no convertir clics automatizados en evidencia de comprensión humana.
7. Capturas desktop y ventana más estrecha, inspección visual, localización y accesibilidad. Completar tests de selectors/advance, builds de los tres proyectos, suites Core/Client, formato y diff check.

La comodidad y comprensión por personas externas aún requieren pruebas con jugadores. La entrega no está terminada mientras estos puntos sigan pendientes.

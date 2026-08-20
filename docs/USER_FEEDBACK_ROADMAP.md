# Roadmap guiado por quejas de usuarios — Football Manager (Reddit)

> **Fecha:** 19 ago 2026 · **Fuente:** investigación con agentes sobre r/footballmanagergames, Steam, foros SI y prensa especializada. Citas verbatim en inglés (texto original de los usuarios); análisis, mapeo y ejemplos de implementación en español.
>
> **Propósito:** la opinión del usuario manda. Cada queja de la comunidad es un requisito de diseño para FootballEngine, con un ejemplo de implementación concreto y coherente con la arquitectura (agentes, `SemanticEvent`, `BalanceConfig`, determinismo, MVU/Elmish, pipeline de calibración).

---

## 0. Metodología y cobertura

| Versión | Estado | Fuente principal | Señales |
|---|---|---|---|
| FM22 | **Pendiente de captura** (agentes cancelados) | — | — |
| FM23 | **Pendiente de captura** (agentes cancelados) | — | — |
| FM24 | ✅ Capturado (15 temas, 40+ citas) | r/footballmanagergames, SI bugtracker, Steam | Threads con 340–1.700 upvotes |
| FM25/FM26 | ✅ Material capturado (agente completó la investigación pero no el informe final; aquí se consolida) | Reddit, Steam, encuesta comunitaria, prensa | Steam ~22% positivas al lanzamiento; encuesta 417 respuestas |

**Corrección de línea temporal crítica** (hallazgo del agente FM25/26):
- **FM25 nunca se lanzó.** Fue **cancelado el 7 feb 2025** tras dos retrasos y anuncios de recorte de funciones (selecciones nacionales, touchline shouts, editor, create-a-club, modo versus, fantasy draft, social feed, data chalkboard; fútbol femenino retrasado). Pre-orders abiertos el 1 oct 2024 **antes** de mostrar gameplay; roadmap de 30 sep 2024 mayormente "TBA".
- **FM26 salió el 4 nov 2025** (beta 23 oct 2025). A agosto 2026 lleva ~9 meses de vida real: lanzamiento con ~22% reseñas positivas en Steam (llegó a ~18%, "el 7º peor juego jamás valorado en Steam", r/footballmanagergames `1op1jq8`), encuesta comunitaria "The Big FM26 Community Survey" (417 respuestas): **78,9% lo considera peor que FM24** (44,8% "mucho peor"), 70,9% UI pobre/muy pobre, 69,3% poca confianza en saves largos, 64,4% inmersión débil, 56,9% satisfechos con el motor de partido.
- Implicación para este roadmap: la serie perdió la confianza del usuario entre 2024 y 2026. **La oportunidad de un juego con causa–efecto real y sin humo es máxima.**

---

## A. Motor de partido — "el juego que se ve no es el juego que se juega"

### 1. Los atributos importan más que la táctica; existe una "respuesta correcta" (gegenpress meta)

**El dolor (verbatim):**
> "Tactics do matter, but only to an extent. I also firmly believe that there actually is a 'right answer' to the tactical question. [...] It's clear that attributes are much more important than tactics."
> — u/EvensenFM, "Testing the viral 'EXPOSING THE FM MATCH ENGINE' Post, Mixed Results" (1.7k upvotes) · reddit.com/r/footballmanagergames/comments/1amwpgd/

> "4-2-4 (2DMs) gegenpress is the most OP tactical framework in FM24"
> — u/ToddH2O, "Why is the future 4-4-2?" · reddit.com/r/footballmanagergames/comments/1gnkips/

**Por qué ocurre en FM:** el motor puntúa resultados con una función oculta de atributos físicos que domina la secuencia de toma de decisión; el entrenador encuentra la combinación de instrucciones que maximiza esa función y la reutiliza en cada save. La táctica es un modificador débil sobre una función fuerte.

**Cómo lo resuelve FootballEngine:** en nuestra arquitectura la decisión no es una función de atributos: es una **cadena causal** `Percepción → Intención → Decisión → Acción → Steering` (PlayerAgent), donde la táctica es el **marco colectivo** (`TeamOrchestrator`) y cada decisión individual retroalimenta el marco (`EmergentLoops`, `Chemistry`). El valor de una opción no lo da una suma ponderada de atributos: el Planner evalúa **consecuencias espaciales** (dónde queda el balón, quién queda libre, xG del estado) ancladas a la utilidad calibrada. Un equipo peor pero mejor posicionado gana duelos de espacio; un equipo con presión descoordinada se abre por la espalda **porque** el orquestador leyó los huecos.

**Ejemplo de implementación:** la regla "ningún número de comportamiento vive fuera de `BalanceConfig`" + el pipeline `Training/` (calibración de pesos). Concretamente:
- Los pesos del Planner (`ProgressionWeight`, `ShootValueWeight`, `DangerBase`) están ya en `WeightSectionsPlanner` y se calibran con datos, no a mano — `Training/` + `weights.json`.
- Nueva suite de tests "meta-proof": un mismo sistema (p.ej. 4-4-2 defensivo vs 4-2-3-1) jugado 500 veces con seeds distintas debe producir distribución de resultados estadísticamente indistinguible entre **jugadores humanos y AI** con la misma calidad de plantilla, y debe **distinguir** equipos de plantilla fuerte vs débil en la medida correcta (los tests `Layer2` de estadísticas ya marcan ventanas de tolerancia).

---

### 2. Saques de esquina y balones parados rotos / explotables

**El dolor (verbatim):**
> "And seemingly 90% of the goals for me, from both teams, come from corners or highlights starting with throw ins. [...] Patch can't come soon enough"
> — usuario borrado, "Is it just me, or are there far too many goals in FM24?" · reddit.com/r/footballmanagergames/comments/181q33o/

> "I have conceded four goals in half a season from the corner taker passing to an attacker on the edge of the box, where said attacker can [...] phone his mom, grandmother and girlfriend to tell them to turn on the tv because he's about to score a goal, and THEN blast the ball through the back of the net, all completely unhindered by any of my defenders."
> — u/Jer0en90, "What set piece instructions have people found useful in FM24?" · reddit.com/r/footballmanagergames/comments/18w3g1w/

**Por qué ocurre en FM:** las rutinas del creador de saques son *scripts* con huecos defensivos que la AI no cierra; el exploit es un guion contra un guion.

**Cómo lo resuelve FootballEngine:** los balones parados **no son scripts** — son jugadores con la misma simulación: la barrera respeta la geometría real de 9,15 m (ley FIFA; `WallPlacer` en `Actions/SetPiece.fs`), el lanzador decide con el mismo `PlayerAgent`, y el defensor que marca usa la misma `Percepción` e `InterceptTable` que en juego abierto. Un "hueco" no se puede programar porque **no existe posición fija**: existe un estado de marcaje evaluado con la misma física de intercepción del juego abierto (`PassIntercept`).

**Ejemplo de implementación:**
- El lanzamiento de córner genera una trayectoria del `BallAgent` idéntica a un pase; la defensa en zona sale del marco de `TeamOrchestrator` (asignación hombre-a-zona), no de una lista de posiciones.
- Test de regresión E2E (capa `Layer1`): en 100 córners con seeds distintas, la tasa de gol debe caer en la ventana calibrada del fútbol real (~2–4%) y **ninguna variante de lanzamiento debe superar el doble de esa tasa** — si una forma de sacar un córner produce más, la causa está en `ShotConfig`/`DangerBase` y se corrige calibrando, no parcheando la rutina.

---

### 3. Porteros que parecen rotos

**El dolor (verbatim):**
> "Yeah, it's SI's inability to program properly. This issue has existed since 2009."
> — u/kidcanary, "Goalkeepers are abysmal in FM24" · reddit.com/r/footballmanagergames/comments/17nvffn/

> "Isnt it simply a difference between match engine and graphic engine? Like, what really happened is keeper never made it this far out of penalty area..."
> — u/matp1, mismo thread

**Por qué ocurre en FM:** el motor de partido y el motor gráfico **no están sincronizados**; lo que ves no es lo que se calculó (la propia comunidad lo diagnostica: "difference between match engine and graphic engine").

**Cómo lo resuelve FootballEngine:** no hay dos motores. El visor renderiza **la simulación**; cada comportamiento emite `SemanticEvent` (principio 7 del AGENTS.md: *lo que no emite evento no existe*). El portero es un agente con decisión propia (`Goalkeeping.fs` ya existe): decide salir/amarrar/despejar leyendo la trayectoria del `BallAgent`, con parámetros en `GKConfig`. Lo que ves es exactamente lo que pasó — el `SemanticEvent` es la única fuente de verdad del render.

**Ejemplo de implementación:** el evento de gol siempre va precedido de la cadena de eventos que lo causó (el `MatchViewer` puede "rebobinar la causa": el pase, el error posicional, la salida del portero). Un test E2E puede **reproducir un gol**: dado el mismo estado previo y la misma decisión del portero, el resultado es idéntico (determinismo) — esto convierte cada clip viral de "portero roto" en un caso de test reproducible o en una decisión justificada por la cadena causal.

---

### 4. Demasiados goles / cada partido es un festival

**El dolor (verbatim):**
> "Four games in with Alcorcon with my good old 442 2DM. Matches so far 5-1, 3-3, 2-4, 3-2. Makes me want to return to the 23."
> — u/titsupagain, "Is it just me, or are there far too many goals in FM24?" · reddit.com/r/footballmanagergames/comments/181q33o/

**Por qué ocurre en FM:** los pesos del motor se equilibran "por sensación"; cada parche mueve la ventana de goles a otro extremo.

**Cómo lo resuelve FootballEngine:** el balance es **dato, no sensación**. El pipeline `Training/` calibra `ShotConfig`, `DuelConfig`, `DefenseConfig` contra ventanas estadísticas del fútbol real, y los tests `Layer2` ("statistical windows stay in tolerance") las hacen cumplir en CI. Cada cambio de pesos tiene una justificación numérica y una regresión.

**Ejemplo de implementación:** `Benchmarks` + scripts de validación (`validate_matches.fsx`): correr 1.000 partidos simulados, comparar goles/partido, xG, disparos fuera, posesión contra la ventana objetivo, y **fallar el build** si se sale. Es la misma respuesta al problema "los parches mueven la ventana": la ventana está en un test, no en un foro.

---

### 5. Lesiones absurdas

**El dolor (verbatim):**
> "My end of season Injury Report showed a total of 89 injuries. EIGHTY-NINE!! In a 46 game season. That's almost an average of two injuries per week."
> — u/lefty3657, "The injury numbers in FM24 are INSANE" (340 upvotes, 95% upvoted) · reddit.com/r/footballmanagergames/comments/17g9uam/

> "SINCE MY LAST MESSAGE I HAVE HAD 4 INJURIES, WHY THE FUCK DO PLAYERS GET VIRUSES AND COLDS EVERY WEEK"
> — u/timsonnen, mismo thread

**Por qué ocurre en FM:** la tasa de lesiones es un número aleatorio por partido, desconectado del historial real de carga del jugador; las lesiones musculares y las "virosis" compiten con la misma ruleta.

**Cómo lo resuelve FootballEngine:** principio 6 del AGENTS.md: *la fatiga que solo afecta a la velocidad no es fatiga real*. Un jugador agotado percibe peor, decide peor y es más conservador. Las lesiones deben ser **consecuencia de carga**: minutos acumulados, intensidad de duelos ganados/perdidos, fatiga del partido. Un jugador descansado se lesiona rarísimo; uno que juega 90' tres veces por semana con presión alta se lesiona con una probabilidad que sube por carga real — y **ambos números viven en `InjuryConfig`** y se calibran contra las ventanas reales (el test de la temporada de 46 partidos sería un test de distribución).

**Ejemplo de implementación:** nueva sección `InjuryConfig` en `BalanceConfig` (tasa base por tipo de lesión, pendiente de carga, factor de superficie/edad) + `Condition` persistente por jugador como estado real (ya existe `frame.Condition`) + test de larga duración en CI: simular 5 temporadas completas (el motor corre 60.000+ ticks por partido; con `runFast` es viable) y comprobar que las tasas quedan en ventana — exactamente el tipo de QA que la comunidad exige ("Good QA should simulate 15-20 years into and try to see what is not working").

---

## B. La AI de los managers — "el juego juega a otro juego"

### 6. AI de traspasos rota: compras redundantes, wonderkids que se pudren

**El dolor (verbatim):**
> "Top/mid table clubs signing average 29-31 year olds for 40m+ for a position they already have two other much better players. These new signings then play 3 matches a season and leave on a loan, then on a free transfer after two seasons. [...] Mathys Tel has played 11 combined matches in 4 years for Bayern"
> — u/yrugay1, "The transfer/youth development AI in FM24 is still terrible" (457 upvotes, 97% upvoted) · reddit.com/r/footballmanagergames/comments/17hbd7r/

> "Leaving a club with a world class squad of 20-26 year olds only to watch the AI sign worse players who are 29+ and completely ruin the squad in a few seasons is so depressing"
> — usuario borrado, mismo thread

**Por qué ocurre en FM:** el mercado AI no planifica con "profundidad por posición y minutos reales de juego"; compra por reputación/CA y la rotación no existe.

**Cómo lo resuelve FootballEngine:** el `ManagerAgent` (con su `SubstitutionIntelligence`) ya piensa en términos de **necesidad real de la plantilla**. La AI de mercado debe usar la misma información que el usuario: minutos jugados por posición, cobertura del rol, edad de la alternativa. Un `SquadPlanner` (patrón `TeamOrchestrator`: lee las decisiones acumuladas) computa "hueco de minutos" por rol cada ventana de mercado y solo entonces busca. Y como el desarrollo depende de **minutos reales jugados** (ver tema 7), un club que compra un wonderkid y no lo juega destruye su activo — la AI tiene incentivo causal de rotar.

**Ejemplo de implementación:**
- `ManagerAgent.transferNeeds` → tabla `(rol, minutos esperados, cobertura actual, perfil de edad)`, todo en config (`TransferConfig`).
- Los fichajes se evalúan contra el hueco: un 29-31 años solo se firma si el hueco es "ganar ahora" (fichaje a corto plazo con presión de directiva alta).
- Test de larga duración: en 10 temporadas simuladas, ningún club debe terminar con >2 jugadores de 25+ sin jugar 10+ partidos/año (el síntoma Tel/Bayern es un invariante de sistema, no un dato anecdótico).

---

### 7. Desarrollo juvenil superficial y newgens de otra época

**El dolor (verbatim):**
> "Newgen CBs and GKs are overwhelmingly incapable of playing out from the back. The number of newgen CBs with good passing, technique, and first touch are miniscule. The number of GKs with those stats are non-existent. Sweeper Keepers are effectively obsolete by 2040."
> — u/SmellyMingeFlaps, "Does anyone else feel like newgens are from a different age?" (408 upvotes, 97%) · reddit.com/r/footballmanagergames/comments/1ix03ej/

> "This sucks, this along with player development after age 23 was the best promised feature of FM24 aside from set pieces, yet they did NOTHING."
> — u/GeeGee889, "The transfer/youth development AI in FM24 is still terrible" · reddit.com/r/footballmanagergames/comments/17hbd7r/

**Por qué ocurre en FM:** el generador de jóvenes no se tocó en años (la comunidad lo dice: "newgens are from a different age") y el desarrollo post-23 es casi nulo.

**Cómo lo resuelve FootballEngine:** la generación de mundo (`WorldGeneration`) es **datos + arquetipos modernos**, no un generador congelado: perfiles por rol (laterales invertidos, centrales con salida de balón, portero líbero) generados con distribuciones paramétricas por arquetipo. El desarrollo es **causal**: minutos jugados, calidad de entrenamiento, edad y rendimiento estadístico real alimentan una curva de desarrollo (`DevelopmentConfig`). Un jugador que juega se desarrolla; uno que no juega, no — y eso es lo que hace coherente al tema 6 (si el juego no lo juega, no rinde el activo).

**Ejemplo de implementación:**
- `WorldGeneration/` — generador de newgens con arquetipos (distribuciones por rol: p.ej. el arquetipo "central moderno" incluye rango alto de pase/first touch).
- `DevelopmentSystem` (en `Engine/Match/Systems/` o en simulación de temporada): tasa de desarrollo ∝ minutos por partido + nivel de liga + edad, con techo por potencial.
- Test de larga duración: a 2040 (15 temporadas), la distribución de arquetipos de la liga debe seguir pareciendo fútbol moderno (ni GKs diminutos ni centrales sin pase en masa).

---

### 8. Los managers AI convergen a un único sistema

**El dolor (verbatim):**
> "in 2043 in my save now and nearly every other manager in the game seems to be using a 4-4-2 now. Nearly every team is using that"
> — u/TheMetaN3rd, "Why is the future 4-4-2?" · reddit.com/r/footballmanagergames/comments/1gnkips/

> "I see 5221 with 2 DMs and 2 AMs all the time. Seems like 33% of the managers in my league use it"
> — u/BogotaLineman, mismo thread

**Por qué ocurre en FM:** el meta del motor filtra hacia el mejor sistema y toda la AI lo adopta (véase tema 1); la diversidad táctica no es un objetivo de diseño.

**Cómo lo resuelve FootballEngine:** cada `ManagerAgent` tiene **personalidad y filosofía** (config de manager: presión, riesgo, estilo); el `TeamOrchestrator` hace emerger el estilo de las decisiones, no de un listado. Y como el balance se calibra contra ventanas (tema 1/4), **ningún sistema tiene un 20% de ventaja sobre el resto** — la diversidad deja de ser castigada. El `AdaptiveSystem` permite además que los managers ajusten contra lo que ven (el antídoto al meta-exploit es que la AI responda a él, como en la vida real).

**Ejemplo de implementación:**
- Perfil de manager en `GameData` (los datos de clubs/países ya viven ahí): `philosophy ∈ {presión-alta, bloque-bajo, posesión, directo}` + `adaptability`.
- Test de larga duración: distribución de sistemas usados por la AI a lo largo de 10 temporadas debe mantener una entropía mínima (no converger a un único sistema), y la reacción de la AI a un exploit debe ser medible (si el usuario abusa de los córners al primer palo, los managers empiezan a defenderlos).

---

### 9. Valoraciones de mercado sin lógica

**El dolor (verbatim):**
> "some brother could be playing in the second divisions and yet we worh 50-70 million it just doesnt make any sense [...] West Ham were relegated [...] mohammed kudus [...] was apparantly worth 90 million euros and BTW he has just 6 g/a in 30 games"
> — u/Single-Revenue-6587, "What is a problem in football manager 2024 that you wish is solved in football manager 2025?" · reddit.com/r/footballmanagergames/comments/1ca4vz9/

> "Form not affecting price is annoying. A guy who's running super hot should definitely have his price rise."
> — usuario borrado, mismo thread

**Por qué ocurre en FM:** el valor se ancla a CA/PA/reputación, no a rendimiento y contrato.

**Cómo lo resuelve FootballEngine:** el valor de mercado es una **función de estado real**: edad, contrato restante, minutos, forma (ventana móvil de estadísticas), contribución de goles, posición y nivel de liga. La misma filosofía de "resultados con causas": un jugador con 6 G/A en 30 partidos en un equipo descendido no vale 90M — el mercado lo lee de las estadísticas, que ya son causales en nuestro motor.

**Ejemplo de implementación:** `MarketValue` en un módulo de mercado (p.ej. `Engine/World/` o `GameData/`): `valor = f(edad, contrato, minutos últimos 12 meses, forma, goles+asistencias, reputación club)` con pesos en `MarketConfig` (BalanceConfig). El "unsettle" funciona porque romper el ánimo del jugador baja su rendimiento y por tanto su valor — todo causal.

---

## C. La gestión humana — "el juego me castiga sin razón"

### 10. Interacciones y moral irracionales

**El dolor (verbatim):**
> "I didn't add a player who has just returned from a loan [...] Gets raging when he's not picked [...] Now 70% of my squad are upset with me and supporting him. [...] And guess what they are angered by that also. Which makes no fucking sense!!!!!!"
> — u/leomv26, "Player Interactions are broken in FM24" (thread viral, bloqueado) · reddit.com/r/footballmanagergames/comments/17yxgiw/

> "After promotion we went unbeaten and clinched for the title. 10 points clear with 4 matchdays to go i gathered the men [...] The players had a meltdown, some of them even crying 'we just got promoted, you can not expect us to win the title :(' The happiness went from excellent to batshit crazy."
> — u/DontbuyFifaPointsFFS, "What is a problem in football manager 2024 that you wish is solved in football manager 2025?" · reddit.com/r/footballmanagergames/comments/1ca4vz9/

> "As I think we all know by now the morale system is totally broken, as you might be blowing by your expectations but lose three games in a row [...] the morale goes from way up to the floor."
> — u/casiotrader, "How do you handle morale bullshit in FM24?" · reddit.com/r/footballmanagergames/comments/181ckfo/

**Por qué ocurre en FM:** el ánimo es un número global que reacciona a disparadores arbitrarios con pendientes violentas; las promesas son textos sin consecuencias causales.

**Cómo lo resuelve FootballEngine:** la moral es **estado causal por jugador**: cada `SemanticEvent` relevante (gol, victoria, titularidad, cesión, promesa cumplida/rota) actualiza el ánimo con un coeficiente calibrado (`MoraleConfig`). Un grupo no "apoya a un quejoso" por contagio mágico: la queja tiene que **tener razón causal** (minutos reales vs expectativa contractual). Promesa = contrato de estado con vencimiento explícito; romperla baja la confianza en el manager de forma medible y explica por qué los compañeros se solidarizan (percepción de injusticia compartida si el afectado cumple su rol). Y nada de pendientes de dos saltos: la calibración hace que 3 derrotas seguidas bajen el ánimo, pero no lo hagan de "excelente a demencial" si la temporada va por encima de lo previsto (la expectativa es contexto, no ruleta).

**Ejemplo de implementación:**
- `State.Promises` / `Morale` por jugador en `GameState`, actualizado en el `EventRouter` (mismo patrón que el plan del equipo).
- `MoraleConfig` en BalanceConfig: coeficientes por evento, factor de expectativa, umbrales de solidaridad.
- Test de sistema (capa `Layer2`): la secuencia "campeón a 4 jornadas del final + 3 derrotas" debe producir una caída acotada; la promesa rota debe producir una caída documentada y recuperable con minutos — el invariante es que **toda caída de moral es explicable por eventos** (rebobinable en el viewer de eventos).

---

### 11. Exigencias contradictorias de la directiva

**El dolor (verbatim):**
> "The board want me too use youth, yet when I do, their in game stats show they are unhappy or anxious..... In the 2nd half of a Cup game [...] it said in the mid game 'mood' stats, 'wish he remained on bench'!!! What kind of footballer, wishes they remained on the bench?"
> — u/Justin2909, "The injury numbers in FM24 are INSANE" · reddit.com/r/footballmanagergames/comments/17g9uam/

**Por qué ocurre en FM:** los objetivos de directiva y las reacciones individuales se calculan en sistemas separados con lógicas contradictorias.

**Cómo lo resuelve FootballEngine:** una sola cadena causal: los objetivos de directiva se derivan de **datos** (reputación del club, presupuesto, posición histórica — `GameData/`), y las reacciones de jugadores usan los mismos hechos. Si el objetivo es "apostar por jóvenes", el joven titular tiene su expectativa contractual alineada; si el club exige ascenso sin presupuesto, la prensa/afición lo espera (contexto del partido), no el jugador que acaba de debutar.

**Ejemplo de implementación:** el objeto `Club` (GameData) expone `expectations` computadas (unión de reputación + finanzas + historial); los `SemanticEvent`s de partido (minutos de juvenil, resultado vs expectativa) alimentan tanto la barra de directiva como el ánimo individual — **una sola fuente de verdad**, imposible de contradecirse.

---

### 12. El juego es demasiado fácil

**El dolor (verbatim):**
> "I just won Serie A with Palermo in season 2 of the save, with the second lowest payroll in the league, and that's a bit much, even for me."
> — u/atomzero, "Is FM 24 too easy?" · reddit.com/r/footballmanagergames/comments/17sbpep/

> "I plugged the tactics I used for FM23 Lincoln City (Gegenpress) - obviously with adjustments for the team and just strolled through to win the title."
> — u/Cesticles, "(FM24) Is it too easy?" · reddit.com/r/footballmanagergames/comments/17fka9u/

**Por qué ocurre en FM:** la dificultad es una consecuencia no intencionada del desbalance (temas 1 y 4): si hay una "respuesta correcta" táctica, el humano la encuentra y la AI no la contradice.

**Cómo lo resuelve FootballEngine:** la dificultad **emerge de la calidad de la AI**, no de modificadores artificiales. Un equipo de Palermo gana la Serie A en la temporada 2 solo si el sistema está calibrado para que eso sea improbable (ventanas estadísticas por nivel de liga — tests `Layer2`). Y como los managers AI **se adaptan** (`AdaptiveSystem` + `ManagerAgent`), repetir el mismo exploit una temporada entera deja de funcionar: la AI ajusta su bloqueo, presión y marcajes — el usuario gana partidos con causas, no con una receta.

**Ejemplo de implementación:** test de balance "ascenso imposible": simular 100 temporadas de un club de presupuesto mínimo con una receta fija contra la AI adaptativa; la tasa de campeonatos debe ser ≤ un umbral calibrado (si no, `Training/` recalibra `ManagerConfig`). El contador de "demasiado fácil" pasa a ser un dato de CI, no una discusión de foro.

---

## D. Producto y confianza — "cada año, la misma promesa rota"

### 13. "FM23 2.0" / innovación inexistente

**El dolor (verbatim):**
> "You made the right decision. This game is bullshit every year. [...] 'Yhea we decided to make FM24 a love letter to the series'. Biggest load of bollocks I've ever heard a dev come out with. [...] A few new fancy features that nobody asks for, and all the same flawed gameplay."
> — u/Stzzla75, "FM24 sucks." · reddit.com/r/footballmanagergames/comments/192s9hx/

> "I'm enjoying the beta but let's be honest, it's FM23 2.0 pretty much."
> — thread "My biggest complaint with the beta/FM24" (eliminado; cita vía snippet de búsqueda)

**Cómo lo resuelve FootballEngine:** no vendemos el juego del año pasado: la propuesta es de **arquitectura** — inteligencia emergente (agentes + orquestador), causalidad total (`SemanticEvent`), determinismo con re-jugabilidad y calibración de datos. Este mismo documento es el roadmap público: cada queja de la comunidad tiene su fila con su causa y su destino de implementación. La innovación no es un comunicado, es la cadena causal del gol.

### 14. La comunidad es el QA

**El dolor (verbatim):**
> "So basically they have no QA team (or they are incompetent), so the customers are the QA team (and u pay for it too)."
> — u/LiteTenkPro, "The transfer/youth development AI in FM24 is still terrible" · reddit.com/r/footballmanagergames/comments/17hbd7r/

> "right? Good QA should simulate 15-20 years into and try to see what is not working."
> — u/DivinityAI, mismo thread

**Cómo lo resuelve FootballEngine:** la simulación es determinista y rápida: **simular 15–20 temporadas es un test de CI**. La suite ya tiene 239 tests (E2E, sistemas, casos límite, determinismo); el roadmap añade los tests de larga duración de los temas 6, 7, 8 y 12. Cada bug reportado tiene forma de test reproducible (`simulación con seed X reproduce el estado Y`), y cada arreglo se envía con su regresión.

### 15. FM25: cancelación, comunicación y confianza (lección de producto)

**El dolor (verbatim y hechos capturados):**
> Fue cancelado el 7 feb 2025 tras anunciar recortes masivos; pre-orders abiertos antes de mostrar gameplay; roadmap "TBA"; radio silencio durante meses (thread r/footballmanagergames `1kt25p8` "why is Sports Interactive still radio silent").
> — PC Gamer sobre la cancelación: "better to be honest and admit a mistake" (la comunidad, aliviada: "fans are weirdly pleased").
> — Miles Jacobson (BBC, vía Operation Sports): "three things went wrong on the same day".

**Cómo lo resuelve FootballEngine:** transparencia por defecto: el roadmap es público y está ligado a tests (este documento), los cambios de balance viven en `weights.json` versionado (cada calibración es un PR revisable), y no hay ciclos de marketing de features anunciadas sin gameplay. Lo que no está en el código no se promete.

### 16. FM26: la UI como primera impresión ("The current UI is indefensible")

**El dolor (verbatim y hechos capturados):**
> "The current UI is indefensible"
> — OP del thread r/footballmanagergames `1oekgsv` (texto vía snippet de búsqueda).

> 70,9% de la encuesta comunitaria (417 respuestas) valora la UI como pobre/muy pobre; SI reconoció oficialmente en su blog de mayo 2026: "the changes we made to the User Interface weren't received well by everyone".
> — "The Big FM26 Community Survey" (ingenuityfantasy.com) · blog oficial FM26 (footballmanager.com/news/return-international-management-and-review-fm26)

> Lista verbatim de lo que falta en FM26 (thread de Steam "Things missing in fm26", `603045243320021568`): reload skin, clear cache, attribute colours, back-button setting, budgets, comparing players/staff, space-to-messages, U21/U19 navigation…

**Cómo lo resuelve FootballEngine:** nuestra UI es **MVU/Elmish sobre Avalonia** (AppState/Slices, Views/Pages): páginas desacopladas del motor, atajos de teclado, tablas densas de datos y temas — la UI es un cliente del motor, no un monolito. La lección real: la UI de datos debe conservar las micro-funciones que los usuarios usan a diario (comparar jugadores, ver presupuesto, navegar juveniles); esas micro-funciones son **roadmap de capa UI pura** (sin tocar el motor) y este documento las registra como requisitos (p.ej. `Views/Pages/SquadPage` debe ofrecer comparador, atajo de mensajes, colores de atributos configurables).

### 17. FM26: "el motor es el mismo" ("95% the same as FM24")

**El dolor (verbatim y hechos capturados):**
> "how is the match engine ?" — hilo de Steam; respuesta citada: "95% the same as FM24"
> — steamcommunity.com/app/3551340/discussions/0/603044859897890639/

> Experimento "all-pace" de Zealand (dic 2025, vía Operation Sports): un equipo sin atributos salvo velocidad compite de tú a tú — el miedo a que solo la velocidad importe sigue vivo.
> — operationsports.com/zealands-all-pace-wolves-experiment-reignites-football-manager-match-engine-fears/

**Cómo lo resuelve FootballEngine:** el motor de partido **es** el juego (arquitectura de 4 capas: contrato → simulador → stepper → agentes; el visor renderiza eventos). No hay capa gráfica que "simule otra cosa". Y el experimento all-pace es directamente un test nuestro: un equipo con solo velocidad debe **perder** ante un equipo equilibrado si el estado de percepción/decisión importa (agentes de 5 capas) — es la afirmación de diseño que se puede verificar en CI con 500 partidos calibrados. La velocidad ayuda a llegar; la decisión decide.

### 18. Confianza y "¿compramos FM27?" (estado actual de la marca)

**Hechos capturados:**
> Steam al lanzamiento de FM26: ~22% positivas (mínimo ~18%) → "7th worst-rated game ever on Steam" (thread r/footballmanagergames `1op1jq8`). Serie histórica: 89–93% positivas.
> Encuesta comunitaria: 69,3% con poca confianza en saves largos; 64,4% inmersión débil; 78,9% peor que FM24.
> Internacional Management volvió como actualización gratuita el 26 may 2026; SI admitió que "the full revamp will not be realised at launch" (blog oficial). FM27 sin anunciar a mayo 2026 (la comunidad se preocupa).

**Cómo lo resuelve FootballEngine:** la confianza se construye con **pruebas visibles**: tests en CI ejecutables por cualquiera (`dotnet run --project FootballEngine.Core.Tests...`), determinismo (misma seed → mismo partido), calibración versionada y un roadmap público con causa (este documento). No hay anuncio de feature sin su test de regresión.

---

## E. Síntesis: qué nos enseña esto (lecciones transversales)

1. **El motor es el producto.** 8 de los 15 temas de FM24 son del motor o de lo que lo alimenta. Todo lo demás (UI, promesas, marketing) es ruido si el partido no convence.
2. **Las consecuencias deben ser visibles.** "Un 2–1 tiene causas encadenadas reales" no es un eslogan: es la función que convierte cada queja de "se siente injusto/roto" en un `SemanticEvent` rebobinable.
3. **El balance es un dato, no un parche.** Ventanas estadísticas en CI + calibración por datos = nunca más "la ventana de goles se mueve con cada parche".
4. **La AI debe jugar al mismo juego.** Rotación, traspasos, desarrollo y adaptación táctica con la misma información causal que el usuario.
5. **Saves largos son el cliente más fiel — y el más castigado.** Newgens, lesiones, convergencia táctica, valoraciones: todo se testea a 10–15 temporadas.
6. **La UI de datos es sagrada.** Las micro-funciones cotidianas son requisitos de capa UI; no se rompen con el motor.
7. **Confianza = transparencia + tests.** La comunidad perdonó la cancelación de FM25 ("better to be honest and admit a mistake"); no perdona el silencio ni las promesas vacías.

---

## F. Roadmap priorizado (deriva de quejas → implementación)

| # | Prioridad | Queja origen | Trabajo | Dónde |
|---|---|---|---|---|
| R1 | **P0** | Motor: atributos>táctica, meta, goles, porteros | Calibrar pesos contra ventanas reales; tests de 1.000 partidos en CI | `Training/`, `weights.json`, tests `Layer2` |
| R2 | **P0** | AI: traspasos/rotación | `SquadPlanner` + necesidades por rol + incentivo causal de rotar | `Engine/Match/Manager/`, `ManagerAgent` |
| R3 | **P0** | Gestión: moral/promesas | Moral causal por `SemanticEvent` + promesas como contrato de estado | `EventRouter`, `MoraleConfig` (BalanceConfig) |
| R4 | **P1** | Saves largos: newgens, lesiones, valoraciones | Arquetipos modernos en generación; lesión=consecuencia de carga; `MarketValue` causal; tests a 15 temporadas | `WorldGeneration/`, `InjuryConfig`/`MarketConfig`, tests `Layer1` |
| R5 | **P1** | AI: convergencia táctica + adaptación | Filosofías de manager en datos + `AdaptiveSystem` anti-meta | `GameData/`, `ManagerAgent` |
| R6 | **P1** | Dificultad | Ventanas de "ascenso improbable" como test de CI | Tests `Layer2`, `ManagerConfig` |
| R7 | **P2** | UI: micro-funciones de datos | Comparador, colores de atributos, atajos, presupuestos; temas | `Views/Pages/`, `AppState/Slices/` |
| R8 | **P2** | Inmersión/control | Touchline shouts como decisiones del `ManagerAgent` visibles en UI | `ManagerAgent` → `Views/Components/MatchViewer` |
| R9 | **P2** | Editor/comunidad | UI del `ModLoader` existente (`GameData/`) | `Views/Pages/`, `GameData/ModLoader.fs` |

---

## G. Fuentes

**FM24 (agente 1, verificado):**
- reddit.com/r/footballmanagergames/comments/1amwpgd/ · /1gnkips/ · /181q33o/ · /18w3g1w/ · /17nvffn/ · /17g9uam/ · /17hbd7r/ · /1ix03ej/ · /1ca4vz9/ · /17sbpep/ · /17fka9u/ · /1fc7lrm/ · /192s9hx/ · /181ckfo/ · /17yxgiw/ (vía Wayback Machine) · /17nro5n/ (eliminado, snippet) · /1dodmqk/
- SI forums/bugtracker: community.sports-interactive.com/forums/topic/578618 · /592383 · /579803 · /582724 · bugtracker r19100
- Steam: steamcommunity.com/app/2252570/discussions/0/4210372179811521436/
- Contexto: realsport101.com (experimento 9 atributos), sortitoutsi.net (press conferences)

**FM25/FM26 (agente 2, capturado):**
- reddit.com/r/footballmanagergames/comments/1ijn8hp/ (megathread cancelación) · /1kt25p8/ (radio silencio) · /1op1jq8/ (7º peor valorado) · /1oekgsv/ (UI indefensible, snippet) · /1r4qbua/ (mejora de dificultad post-16.1.3)
- Steam: app 3551340 discussions (603044859897683767 "absolutely shocking" · 603045243320021568 "Things missing in fm26" · 603044859897890639 "how is the match engine ?" · 690866958234625968 editor)
- Encuesta: ingenuityfantasy.com/feature-articles/the-big-fm26-community-survey-results/ · tactical exploits
- Prensa: theguardian.com (cancelación 7 feb 2025) · ign.com (cancelación) · pcgamer.com (cancelación) · eurogamer.net (FM25 no es continuación) · thesixthaxis.com (selecciones fuera) · metro.co.uk ("hot mess") · dexerto.com (pre-orders) · operationsports.com (Miles Jacobson; experimento all-pace) · frvr.com (peor valorado) · thehighertempopress.com (beta FM26; FM27) · fullerfm.com (selecciones) · sportsweek.org (specs)
- Oficial: footballmanager.com/news/return-international-management-and-review-fm26

**Pendiente de captura:** FM22 y FM23 (agentes cancelados). Los temas capturados de FM24 son consistentes con el patrón histórico de la serie (motor, AI, moral, UI); cuando se capturen, se añadirán como sección propia.

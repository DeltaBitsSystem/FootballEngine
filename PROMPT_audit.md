# Prompt para auditoría del motor de fútbol

---

Tengo un motor de fútbol determinista en F# (.NET 10). Hay dos problemas graves que investigar y arreglar. Te voy a dar los JSONs de 10 partidos simulados.

**Problema 1: Self-passes.** Existe un tipo opaco `PassTarget` cuyo constructor privado debería impedir self-passes a nivel compile-time, pero los JSONs muestran `PassLaunched (11, 11)`, `PassLaunched (1, 1)`, etc. Significa que hay caminos en el código que emiten pases sin pasar por `PassTarget.create`.

**Problema 2: 0 goles en 10 partidos.** El engine produce tiros (xG bajísimos ~0.01), pero ninguno convierte. Puede ser calibración de tiros, decisiones de disparo, o algo estructural.

Investiga los JSONs y el código fuente. Devuélveme un plan en markdown con:
1. Diagnóstico de cada problema (qué encontraste en los datos)
2. Archivos del engine involucrados y por qué fallan
3. Fixes propuestos en orden de aplicación, uno por archivo
4. Criterio de verificación (comando + resultado esperado)

# Macro Deck + MyInstants

Versión modificada de [Macro Deck](https://github.com/Macro-Deck-App/Macro-Deck) (aplicación de escritorio) y de su
[cliente móvil](https://github.com/Macro-Deck-App/Macro-Deck-Client-App), con estas incorporaciones:

- **Soundboard de MyInstants**: una pestaña en la app de escritorio y un tipo de carpeta del deck que muestra los
  sonidos de [myinstants.com](https://www.myinstants.com) (tendencias o búsqueda) y los reproduce en el PC.
  Paginado, con filas/columnas configurables, botón de parar y actualización automática de sonidos nuevos.
  MyInstants no ofrece API: los sonidos se leen de su web, así que puede romperse si el sitio cambia.
- **Cliente Android legacy** (`Macro-Deck-Client-App/android-legacy`): app nativa en Java para dispositivos muy
  antiguos (Android 4.2 / API 17), sin dependencias. Incluye la vista del soundboard con búsqueda.
- **Sliders**: pestaña y carpetas de sliders (p. ej. SteelSeries Sonar).

## Estructura

| Carpeta | Contenido |
| --- | --- |
| `Macro-Deck/` | App de escritorio (.NET 10, WinForms/WPF). Código del soundboard en `src/MacroDeck/Soundboard/` |
| `Macro-Deck-Client-App/` | Cliente Ionic/Angular y el cliente `android-legacy` |

## Uso del soundboard

1. En el escritorio, crea una carpeta y marca **Soundboard folder**.
2. Añade un botón con la acción *Ir a la carpeta seleccionada* que apunte a ella.
3. Desde un cliente, ese botón abre el panel de sonidos; en la tablet legacy hay botones de página, parar,
   filas/columnas y búsqueda.

## Licencias

El código original conserva su licencia: Macro Deck (Apache-2.0, ver `Macro-Deck/LICENSE`) y el cliente
(MIT, ver `Macro-Deck-Client-App/LICENSE`). Los sonidos pertenecen a sus autores y a MyInstants; este proyecto no
los incluye ni los redistribuye.

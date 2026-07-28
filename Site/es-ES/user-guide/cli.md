# Búsqueda por línea de comandos (slf)

SwiftList también incluye un pequeño complemento de línea de comandos, **`slf`** — un buscador difuso al estilo
fzf que busca en el mismo índice que ya mantiene la propia App, en lugar de duplicar nada de esa configuración. Es
para cualquiera que viva en una terminal y quiera tener también ahí disponible la búsqueda de SwiftList
(coincidencia difusa, alias en pinyin, unidades de red, todo), no solo en las ventanas de búsqueda.

`slf` necesita que la App de SwiftList ya esté en ejecución — se comunica con la App a través de una pipe local,
por usuario, en lugar de volver a escanear nada por sí mismo. Si la App no está en ejecución, `slf` falla de
inmediato con un error en stderr en lugar de quedarse colgado.

## Configurarlo

`slf.exe` se instala junto con la App. Marca **Añadir la herramienta de búsqueda por línea de comandos slf al
PATH** en la página de selección de tareas del instalador para poder ejecutarlo como `slf` desde cualquier
terminal — esto añade la carpeta de instalación de SwiftList a tu PATH del sistema. Abre una nueva ventana de
terminal después; una que ya estuviera abierta cuando instalaste no detectará el cambio.

Si te saltaste esa opción, aún puedes ejecutarlo directamente desde donde esté instalado SwiftList.

## Uso básico

```
slf
```

abre un selector interactivo: escribe para filtrar de forma difusa, exactamente igual que la propia búsqueda de la
App — incluida la coincidencia de alias en pinyin para nombres de archivo en chino (ver [Sintaxis de
búsqueda](./search-syntax)).

| Tecla | Acción |
|---|---|
| Escribir | Filtrar resultados |
| ↑ / ↓ | Mover el resaltado |
| Re Pág / Av Pág | Saltar una página cada vez |
| ← / → | Mover el cursor de texto dentro de la consulta |
| Tab | Marcar/desmarcar el resultado resaltado (las filas marcadas muestran `*`) |
| Intro | Imprimir la(s) ruta(s) seleccionada(s) — o solo la resaltada, si no hay ninguna marcada — y salir |
| Esc / Ctrl+C | Salir sin imprimir nada |

## Rellenar la consulta de antemano

```
slf report
```

y

```
echo report | slf
```

ambos se abren ya filtrados a `report`. En cualquier caso, esto solo rellena de antemano el cuadro de consulta y
empieza la misma búsqueda que escribirlo produciría — nunca selecciona ni imprime automáticamente un resultado por
sí mismo, así que sigues teniendo que navegar y pulsar Intro/Tab tú mismo.

## Seleccionar varios resultados

Tab marca o desmarca la fila resaltada. Las filas marcadas persisten incluso después de cambiar la consulta — así
que puedes buscar un archivo, marcarlo, buscar otra cosa, marcarla también, y así sucesivamente. La línea de
estado muestra cuántos hay marcados en ese momento. Pulsar Intro mientras algo está marcado imprime todas las
rutas marcadas, una por línea, sin importar qué esté resaltado en ese momento.

## Usar el resultado en otro comando

El selector interactivo de `slf` se dibuja directamente en la consola, nunca a través de los flujos normales de
entrada/salida — lo único que llega a stdout es la(s) ruta(s) finalmente seleccionada(s), una por línea, impresas
al pulsar Intro. Eso es lo que permite que su salida se combine con las técnicas habituales de la shell para
capturar el resultado de otro comando.

PowerShell:

```powershell
code (slf)
$path = slf; code $path
```

cmd.exe (sin sustitución de comandos integrada — usa `for /f`):

```cmd
for /f "delims=" %i in ('slf') do code "%i"
```

## Limitaciones

- Sin panel de vista previa — deliberadamente fuera de alcance, ya que el propio [Menú de acciones y vista
  previa](./actions-and-preview) de la App ya cubre eso.
- Requiere que la App de SwiftList esté en ejecución; `slf` no indexa nada por sí mismo.

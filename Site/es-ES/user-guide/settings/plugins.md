# Plugins

Lista cada plugin instalado, con la versión del SDK de Plugins cargada actualmente mostrada como una insignia en
la cabecera de la página (relevante si estás comparando un plugin con el [Manual de
Desarrollador](../../dev-guide/)).

## Tarjeta por plugin

Cada plugin instalado obtiene una tarjeta que muestra su icono, nombre, versión, nombre de archivo de la DLL, y
una **descripción general de la función**.

Haz clic en la tarjeta para expandirla y ver sus componentes registrados, agrupados por tipo (proveedores de
búsqueda, proveedores de menú dinámico, etc.) — cada componente activable tiene su propia **casilla de
habilitar/deshabilitar**; un componente marcado como obligatorio muestra en su lugar un icono de candado y no se
puede desactivar. Pasar el cursor sobre un componente revela su **tooltip detallado de función**.

Cuando un grupo (o el plugin en su conjunto) tiene más de un componente activable, aparece un enlace
**Seleccionar todo / Deseleccionar todo** junto a su encabezado, que permite marcar/desmarcar todas las casillas
de ese ámbito a la vez en lugar de una por una.

Si un plugin expone su propia configuración (ajustes personalizados más allá de un simple
habilitar/deshabilitar), aparece un botón **Configurar** en la cabecera de la tarjeta, que abre el propio diálogo
de configuración de ese plugin.

Un banner en la parte inferior de la página recuerda que algunos interruptores de componente solo surten efecto
tras reiniciar SwiftList.

Si no hay ningún plugin instalado, la página muestra en su lugar un mensaje de estado vacío.

Para ver un ejemplo concreto de cómo es en la práctica el propio diálogo **Configurar** de un plugin (por ejemplo,
cambiar una palabra clave de activación), ver [Respuestas instantáneas y atajos de palabra
clave](../instant-answers).

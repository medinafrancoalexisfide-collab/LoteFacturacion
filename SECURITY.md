# Política de seguridad

Este programa maneja **certificados digitales de ARCA, credenciales y datos de facturación**. Por eso la seguridad es parte del diseño y se documenta acá qué protege y qué no.

## Cómo reportar un problema de seguridad

**No abras un issue público** con detalles de una vulnerabilidad.

Usá **GitHub → pestaña Security → "Report a vulnerability"** (reporte privado). Si no está disponible, contactá a la persona que mantiene el repositorio por el medio indicado en su perfil de GitHub.

Es un proyecto personal y de aprendizaje: se responde en la medida de lo posible, sin plazos garantizados.

## Versiones con soporte

Solo la última versión publicada en la rama `main`.

## Qué protege el programa

| Dato | Dónde vive | Protección |
|---|---|---|
| Contraseña y client secret de Don Gestión | `settings.json` | Cifrados con DPAPI (usuario de Windows actual) |
| Contraseña del certificado y del e-mail | `settings.json` | Cifradas con DPAPI |
| Ticket de acceso de ARCA (token + firma) | `ta_*.dat` | Cifrado con DPAPI |
| Certificado `.pfx` | Ruta elegida por el usuario | **No** lo copia ni lo guarda el programa; se lee desde donde esté |
| Clientes, importes, CAE | `datos.db` (SQLite) | Permisos de Windows. **Sin cifrado propio** |
| PDF de facturas | `Documentos\FacturacionLote\Facturas` | Permisos de Windows |
| Registro de actividad | `%LocalAppData%\FacturacionLote\logs` | Oculta e-mails, números largos (CUIT, CAE, DNI) y tokens; se borra a los 30 días |

DPAPI protege contra que alguien copie los archivos a otra PC u otro usuario. **No** protege contra malware que corra con la sesión del mismo usuario de Windows.

## Controles que evitan errores costosos

- Arranca siempre en **homologación**; producción se activa a mano en `settings.json`.
- Cada lote en producción exige **escribir `PRODUCCION`** en una ventana de confirmación (una vez por lote).
- El envío de e-mails a clientes reales exige dos ajustes manuales **y** escribir `ENVIAR`.
- Ambas confirmaciones vienen activadas (`"ConfirmacionReforzada": true`). Desactivarlas en `settings.json` vuelve al cartel Sí/No y baja esta protección: no se recomienda.
- Las direcciones de ARCA (`ArcaUrlWsaa`, `ArcaUrlWsfe`) solo aceptan `https`, dominios `*.afip.gov.ar` / `*.arca.gob.ar` y deben coincidir con el ambiente (una dirección de producción en homologación se rechaza, y al revés). Así, editar `settings.json` no puede desviar la firma ni los tokens a un servidor ajeno.
- Las respuestas XML de ARCA se leen con DTD prohibido (sin entidades externas).
- Todo valor que viene de afuera (Don Gestión, el usuario) llega a SQLite como **parámetro**; el SQL armado como texto solo usa constantes internas y un número entero.
- Los pedidos SOAP solo incluyen valores numéricos o devueltos por ARCA, no texto libre del usuario ni de Don Gestión.
- Protección contra duplicados: índice único en SQLite y reserva en estado "Enviando" antes de llamar a ARCA.

## Limitaciones conocidas

- La base `datos.db` no está cifrada. Recomendado: BitLocker o cifrado de disco. Cifrarla (por ejemplo, con SQLCipher) está pendiente.
- El instalador no está firmado digitalmente.
- El archivo de registro contiene nombres de clientes y números de comprobante (no e-mails ni CUIT completos).
- Las dependencias se revisan con la auditoría de NuGet, Dependabot y la acción de CI, pero no hay un análisis de la cadena de suministro más profundo.
- Es un prototipo: no tuvo una auditoría de seguridad independiente.

## Si se filtra algo

- **Certificado `.pfx` o su contraseña:** revocalo desde *Administración de Certificados Digitales* en el sitio de ARCA y generá uno nuevo.
- **Credenciales de Don Gestión:** cambiá la contraseña del usuario de API y regenerá el client secret.
- **Contraseña de aplicación de Gmail:** revocala desde la cuenta de Google y creá otra.
- **Algo subido por error a Git:** no alcanza con borrarlo en un commit nuevo; el secreto queda en el historial. Revocalo primero y recién después limpiá el historial.

## Para quien contribuya

- No subas `settings.json`, `*.db`, `*.pfx`, PDF ni capturas con datos reales de clientes. El `.gitignore` los excluye, pero revisá antes de cada commit.
- Activá en el repositorio: *Secret scanning*, *Push protection*, *Private vulnerability reporting* y *Code scanning* (CodeQL).

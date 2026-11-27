# Facturación por lote (ARCA + Don Gestión)

Aplicación de escritorio (Windows, WinForms, .NET 10) para **emitir facturas electrónicas por lote** a partir de las ventas del mes anterior cargadas en *Don Gestión*, usando los web services de ARCA (WSAA + WSFEv1), sin librerías externas de ARCA: el SOAP está escrito a mano.

> **Estado: prototipo (v0.3).** Probado en el ambiente de **homologación** de ARCA. La emisión de **Factura C** (monotributo) se probó de punta a punta; las **Facturas A y B** (Responsable Inscripto) están implementadas siguiendo la especificación, pero **todavía no se probaron de punta a punta** (hace falta el certificado de homologación de un Responsable Inscripto). No es asesoramiento fiscal ni contable, y **no está afiliado a ARCA ni a Don Gestión**. Antes de usarlo en producción, revisalo con tu contador.

## Qué hace

- Lee clientes y ventas de Don Gestión (API REST, OAuth2).
- Arma la lista de facturas del período: servicio del mes anterior, vencimiento el último día del mes de emisión.
- Según la **condición del emisor**:
  - **Monotributista:** emite Factura C.
  - **Responsable Inscripto:** emite Factura **A o B** según la condición de IVA de cada cliente, separando neto e IVA (alícuota configurable, 21 % por defecto).
- La condición de IVA de cada cliente sale de lo ya guardado y, si no hay, se sugiere desde el tipo de factura cargado en Don Gestión. Si no hay ningún dato, el programa **no adivina**: la fila queda pendiente hasta elegirla.
- Permite destildar clientes (la selección queda guardada) y filtrar la lista (todo / solo pendientes / solo facturadas / solo con problemas).
- Emite en ARCA y guarda CAE, estado y un PDF con código QR.
- Historial con filtro por estado.
- Envía las facturas por e-mail (SMTP) en **modo prueba**: muestra el e-mail de cada cliente, pero envía todo a una casilla de prueba.
- **Protege contra duplicados** (índice único en SQLite + reserva "Enviando" antes de llamar a ARCA) y **verifica con ARCA** los comprobantes que hayan quedado en estado incierto tras un corte de red. La emisión es estrictamente secuencial por la numeración correlativa.

## Cómo funciona (resumen)

```
Don Gestión (API) ──► Lista de clientes y ventas del mes anterior
                           │  (el usuario destilda lo que no se factura)
                           ▼
              Reserva en SQLite (estado "Enviando")
                           ▼
        ARCA: WSAA (login con certificado) ─► WSFEv1 (CAE)
                           ▼
        SQLite (Autorizada / Error) ─► PDF con QR ─► e-mail
```

## Estructura

```
src/FacturacionLote/
├─ Config/           AppSettings (ajustes, rutas, secretos cifrados con DPAPI)
├─ Domain/           Cuit, Catalogos, TiposComprobante, CalculoIva, DTOs de Don Gestión, filas de la UI
├─ Infrastructure/   DonGestionClient, ArcaClient, ArcaEndpoints, XmlSeguro, Registro/Redactor, Database (SQLite)
├─ Services/         BillingService (+ .Emision), FacturaPdfService, EmailService
└─ UI/               MainForm, SettingsForm, HistorialForm, EnvioEmailForm, ConfirmacionEscrita
tests/FacturacionLote.Tests/   pruebas unitarias (xUnit)
installer/                     instalador.ps1 + FacturacionLote.iss (Inno Setup)
```

## Requisitos

- Windows y .NET 10 SDK (solo para compilar; el instalador incluye .NET).
- Una cuenta de Don Gestión con acceso a la API (client id, client secret, usuario y contraseña).
- Un certificado de ARCA (`.pfx`) habilitado para el servicio `wsfe` y un punto de venta de tipo *Web Services*. Para probar, usá el ambiente de homologación.

## Cómo ejecutarlo

```
dotnet build
dotnet run --project src/FacturacionLote
```

Al abrir el programa por primera vez se pide la configuración (botón **Configuración**). Los datos se guardan fuera del repositorio, en `%LocalAppData%\FacturacionLote`.

## Pruebas

```
dotnet test
```

Cubren las reglas de negocio (período, IVA, CUIT, tipo de comprobante), el cifrado de secretos, la validación de direcciones de ARCA, el parseo seguro de XML y el ocultamiento de datos personales en el registro. **No** llaman a ARCA ni a Don Gestión. Se ejecutan en Windows porque usan DPAPI.

## Instalador

Con [Inno Setup](https://jrsoftware.org/isdl.php) instalado, desde la carpeta `installer`:

```
powershell -ExecutionPolicy Bypass -File .\instalador.ps1
```

Publica el programa (autocontenido, Windows x64) y genera `installer\salida\Instalar-FacturacionPorLote-0.3.0.exe`. El instalador **no está firmado digitalmente**, así que Windows puede mostrar el aviso "Windows protegió su equipo".

## Envío de facturas por e-mail

- En **Configuración → Envío de facturas por e-mail (SMTP)** se cargan servidor, puerto, usuario (la propia dirección, que también es el remitente), contraseña y una casilla de prueba. Con Gmail hace falta una **contraseña de aplicación** (requiere verificación en 2 pasos).
- **Por defecto NUNCA se envía a clientes**: todo va a la casilla de prueba. El envío real exige activar a mano, en `settings.json`, `"UsarProduccion": true` **y** `"EmailEnviarAClientes": true`, y además escribir `ENVIAR` en una ventana de confirmación (salvo que `ConfirmacionReforzada` sea `false`).
- Cada envío queda registrado en la tabla `EnvioEmail` de SQLite.

## Seguridad

Resumen (el detalle y cómo reportar un problema están en [SECURITY.md](SECURITY.md)):

- Contraseñas, client secret y contraseña de e-mail: **cifrados con DPAPI** (solo el mismo usuario de Windows en la misma PC los puede leer).
- El ticket de acceso de ARCA (token y firma) se guarda **cifrado con DPAPI**.
- Siempre arranca en **homologación**. Pasar a producción es deliberado: se edita `settings.json` y cada lote exige escribir `PRODUCCION` (una vez por lote, no por factura). Quien prefiera el cartel Sí/No puede poner `"ConfirmacionReforzada": false`.
- Las direcciones de ARCA solo pueden cambiarse a dominios oficiales, con https y coherentes con el ambiente.
- Las respuestas XML se leen sin DTD (sin entidades externas).
- El registro en archivo oculta e-mails, CUIT y tokens.
- Auditoría de paquetes NuGet, Dependabot, escaneo de secretos y CI en GitHub.
- El repositorio **no contiene** credenciales, certificados ni datos reales.

## Limitaciones conocidas

- Factura A/B sin probar de punta a punta contra ARCA.
- Cuando un total no se puede separar en neto + IVA exactos (alrededor de 1 de cada 6 importes con IVA 21 %), el IVA absorbe la diferencia de centavos. **No está verificado cuánta diferencia tolera ARCA**; hay que probarlo en homologación.
- Sin notas de crédito ni de débito.
- Una sola alícuota de IVA por venta.
- No carga las facturas emitidas de vuelta en Don Gestión.
- La base `datos.db` (clientes, importes, CAE) **no está cifrada**; se protege con los permisos del usuario de Windows (conviene BitLocker).
- Las pruebas automáticas no cubren la interfaz ni las llamadas reales a ARCA.

## Licencia

[MIT](LICENSE). Los PDF se generan con [QuestPDF](https://www.questpdf.com/) bajo su licencia *Community*; revisá sus términos antes de redistribuir o usar comercialmente.

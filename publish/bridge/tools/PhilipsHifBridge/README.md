# PhilipsHifBridge

Windows-only verification bridge for Philips PIC iX / HIF ADT integration.

## Purpose

The existing gateway can complete the low-level Net.TCP/WCF handshake, but PIC iX rejects ADT messages sent back on that hand-written connection with `ContractFilter mismatch`.

This bridge uses the actual Philips DLL contracts and Microsoft WCF instead of hand-writing Net.TCP frames. It is a diagnostic bridge, not a proven production adapter yet.

## Verified Findings

The bridge has reproduced the core PPIS workflow used by PIC iX patient identity integration:

- PIC iX subscribes to `IPIDuplexService`.
- The subscription name received from PIC iX is:
  `PICIX_B_Philips.PIC.PatientDataServices.PatientService`.
- The bridge can call `IPIClientCallback.OnPIChange(...)`.
- PIC iX can return `OnPIChange returned True`.
- Patients posted to the bridge can be found from PIC iX patient search.
- Manual search in PIC iX triggers `SearchPatient(...)` on the bridge.

This means the useful integration path is not plain HL7 delivery to PIC iX. The useful path is a minimal HIF/PPIS patient identity service:

1. Convert ADT to `PIChange` / `PatientIdentity`.
2. Notify PIC iX with `OnPIChange`.
3. Return patient identity records through `SearchPatient`.

More detail is recorded in:

```text
docs/Philips-HIF-PPIS-Bridge.md
```

## What It Hosts

- `IPIDuplexService` at:
  `net.tcp://<host>:9912/Philips.HIF.Services.PpisServiceDuplex/Philips.HIF.Contracts.IPIDuplexService`
- `IPatientIdentity` at:
  `net.tcp://<host>:9912/Philips.HIF.Services.PpisService/Philips.HIF.Contracts.IPatientIdentity`
- local HTTP test endpoint:
  `http://localhost:5080/adt`

When PIC iX subscribes to `IPIDuplexService`, the bridge stores the real WCF callback channel. Posting ADT XML/HL7 to `/adt` calls `IPIClientCallback.OnPIChange(change, descriptor)` through Microsoft WCF.

## Build On Windows

Requirements:

- Windows
- .NET Framework 4.7.2 developer pack or Visual Studio Build Tools
- Philips DLLs in the repository `dll` folder

From an Administrator Developer Command Prompt:

```bat
cd tools\PhilipsHifBridge
build.bat
```

## Run

Stop `HL7GatewayService` first if it is already using port `9912`.

```bat
run.bat
```

If PIC iX is on another machine, the Net.TCP endpoint must use this bridge computer's real IPv4 address, not `localhost`.

The root launcher can auto-detect the local IPv4 address:

```bat
run-bridge.bat
```

Or specify it explicitly:

```bat
run-bridge.bat 192.168.31.223
```

Then configure PIC iX inbound ADT/HIF target to this machine on port `9912`.

By default the bridge tries to read the main middleware SQL Server connection
string from the service `appsettings.json`:

```json
"ConnectionStrings": {
  "SqlServer": "..."
}
```

You can override it with an environment variable:

```bat
set HL7GATEWAY_SQLSERVER=Server=...;Database=MedicalIntegrationGateway;...
run-bridge.bat 192.168.31.223
```

Or pass it directly to the executable:

```bat
PhilipsHifBridge.exe --tcp net.tcp://192.168.31.223:9912/ --http http://localhost:5080/ --sql "Server=...;Database=MedicalIntegrationGateway;..."
```

## Windows Service Mode

Console mode is still recommended while debugging because it shows the live bridge logs directly:

```bat
run-bridge.bat 192.168.31.223
```

After the bridge is stable, install it as a Windows service:

```bat
build-bridge.bat
install-bridge-service.bat 192.168.31.223
```

If you are already inside `tools\PhilipsHifBridge`, use:

```bat
build.bat
install-service.bat 192.168.31.223
```

The installed service name is:

```text
PhilipsHifBridge
```

Uninstall:

```bat
uninstall-bridge-service.bat
```

Or from `tools\PhilipsHifBridge`:

```bat
uninstall-service.bat
```

Do not run console mode and service mode at the same time. Both listen on port `9912`.

## Test

1. Wait for console output:

```text
[HIF] PIC iX subscribed: name=...
```

2. Check status:

```bat
curl http://localhost:5080/status
```

Expected:

```text
subscriber=True; name=...
```

3. POST one ADT XML or HL7 body:

```bat
curl -X POST --data-binary @adt.xml http://localhost:5080/adt
```

4. Read runtime logs:

```bat
curl http://localhost:5080/logs?format=text
```

Interpretation:

- HTTP 200 and `OnPIChange returned True`: PIC iX accepted the real WCF callback path.
- `SearchPatient called; returning 1 patient(s)`: PIC iX pulled patient details from the bridge.
- HTTP 502 with a WCF exception: PIC iX subscribed, but rejected the callback/business message.
- No subscription appears: PIC iX did not accept this bridge endpoint/binding, or port/firewall/config is wrong.

## Runtime Logs

The bridge writes detailed PPIS/HIF events to three places:

- console
- `bin\Release\bridge.log`
- HTTP endpoint `GET http://localhost:5080/logs`

Useful endpoints:

```text
GET http://localhost:5080/status
GET http://localhost:5080/logs?sinceId=0&take=200
GET http://localhost:5080/logs?format=text
POST http://localhost:5080/adt
```

HL7Gateway's ADT page proxies this data and shows it in the bridge log panel.

## Patient Persistence

The bridge now uses the main HL7Gateway SQL Server database as the primary
patient store. It reads and writes:

```text
dbo.Patients
dbo.Visits
```

Field mapping:

- MRN -> `Patients.PatientId`
- Patient name -> `Patients.Name`
- DOB/gender -> `Patients.DateOfBirth` / `Patients.Gender`
- Visit number -> `Visits.VisitId`
- Care unit -> `Visits.Department`
- Facility/ward -> `Visits.Ward`
- Room -> `Visits.Room`
- Bed -> `Visits.Bed`

On startup the bridge loads patients from SQL Server and rebuilds Philips
`PatientIdentity` objects for `SearchPatient`.

If SQL Server is not configured or temporarily fails, the bridge falls back to
the local JSON cache:

```text
bin\Release\patients.json
```

The fallback exists only to keep field testing possible during database/network
problems. In normal deployment, `/status` should show:

```text
storageMode=SqlServer; store=SQL Server: <server>/<database>
```

## Current Boundary

This bridge cannot be fully validated on macOS because Mono does not implement the reliable-session WCF pieces that Philips uses. It must be tested on Windows with full .NET Framework WCF.

The current bridge proves subscription, notification, and manual patient search. It does not yet prove automatic bedside patient assignment/update after `OnPIChange`; that may require PIC iX Auto ADT settings, exact bed mapping, or additional PPIS change semantics.

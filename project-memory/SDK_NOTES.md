# VRChat-SDK: was das Tool davon benutzt

Stand: 13. November 2026. Aus den SDK-Paketen selbst gelesen, nicht aus der
Erinnerung: `com.vrchat.base` und `com.vrchat.avatars` in den Versionen 3.3.0,
3.4.0, 3.5.0, 3.6.0, 3.7.0, 3.7.6 (nur base), 3.8.0, 3.8.1, 3.9.0, 3.10.0 und
3.10.6 (aktuell). Nichts davon ist in Unity kompiliert oder ausgeführt.

## Assemblies

Seit 3.3.0 unverändert, jeweils als Assembly Definition im Quelltext:

| Assembly | Paket | Plattform |
| --- | --- | --- |
| `VRC.SDKBase` | base | alle |
| `VRC.SDKBase.Editor` | base | Editor |
| `VRC.SDK3A` | avatars | alle |
| `VRC.SDK3A.Editor` | avatars | Editor |

`VRC.SDK3A.Editor` referenziert selbst `VRC.SDK3A`, `VRC.SDKBase.Editor`,
`VRC.SDKBase`, `VRC.Enums.Validation.Performance` und `UniTask`. VRChats Doku
nennt für fremde Pakete nur die beiden Editor-Assemblies; in 3.8.1-beta.1
reichte das nicht (ein Parametertyp lag in `VRC.SDK3A`, Fremd-Tools brachen mit
CS0012). Deshalb alle vier referenzieren.

Die vorkompilierten DLLs (`VRCCore-Editor.dll`, `VRCSDKBase.dll`,
`VRCSDK3A.dll`, `VRC.SDK3.Dynamics.*.dll` …) sind in 3.10.6 alle als „auto
referenced" markiert. Eine eigene Assembly mit `overrideReferences: false`
bekommt sie ohne Eintrag.

## Wo die benutzten Typen liegen (3.10.6)

| Typ | Namespace | Liegt in |
| --- | --- | --- |
| `VRCSdkControlPanel` | (global) | `VRC.SDKBase.Editor` |
| `IVRCSdkBuilderApi` | `VRC.SDKBase.Editor` | `VRC.SDKBase.Editor` |
| `IVRCSdkAvatarBuilderApi` | `VRC.SDK3A.Editor` | `VRC.SDK3A.Editor` |
| `VRCApi`, `VRCAvatar` | `VRC.SDKBase.Editor.Api` | `VRC.SDKBase.Editor` |
| `VRCCopyrightAgreement` | `VRC.SDKBase` | `VRC.SDKBase.Editor` |
| `PipelineManager`, `APIUser` | `VRC.Core` | `VRCCore-Editor.dll` |
| `VRCAvatarDescriptor`, `VRCExpressionParameters` | `VRC.SDK3.Avatars.*` | `VRCSDK3A.dll` |
| Contacts | `VRC.SDK3.Dynamics.Contact.*` | `VRC.SDK3.Dynamics.Contact.dll` |

Das erklärt den CS0246 vom ersten Unity-Test: `VRCCopyrightAgreement` liegt im
Namespace `VRC.SDKBase`, nicht in `VRC.Core` oder `…Editor.Api`.

## Signaturen und Versionsgrenzen

- `Task BuildAndUpload(GameObject target, VRCAvatar avatar, string thumbnailPath = null, CancellationToken cancellationToken = default)`
  — unverändert seit 3.3.0. Ab 3.8.0 zusätzlich `BuildAndUploadMultiPlatform`,
  ab 3.8.1 Varianten mit `List<PerPlatformOverrides.Option>`.
- `Task<string> Build(GameObject target)` — seit 3.3.0; liefert den Bundle-Pfad.
- `VRCApi`: `GetAvatar`, `UpdateAvatarInfo`, `UpdateAvatarImage`,
  `UpdateAvatarBundle`, `CreateNewAvatar` seit 3.3.0; ab 3.9.0 dazu
  `CreateAvatarRecord`, `DeleteAvatar`, `GetAvatarStyles`. Signatur in 3.10.6
  (über die Versionen nicht verglichen):
  `UpdateAvatarImage(string id, VRCAvatar data, string pathToImage, Action<string, float> onProgress = null, CancellationToken cancellationToken = default)`.
- `VRCCopyrightAgreement`: **erst ab 3.8.0**. `public static class`, darin
  `internal static async Task<bool> Agree(string contentId)` (passt zum
  Reflection-Aufruf in `SdkCompat`). In 3.7.6 kommt der Name in beiden Paketen
  weder im Quelltext noch in einer DLL vor. Das veröffentlichte Tool v3.3.0 mit
  `typeof(VRCCopyrightAgreement)` kompiliert unter SDK < 3.8.0 also gar nicht.
- `IVRCSdkBuilderApi` hat in 3.10.6 öffentliche Ereignisse `OnSdkBuildStart/Progress/
  Finish/Success/Error`, `OnSdkUploadStart/Progress/Finish/Success/Error` sowie
  `BuildState`, `UploadState`, `CancelUpload()`.

**Folgerung Mindestversion:** `com.vrchat.avatars >= 3.8.0`. Wirklich
ausgeführt ist das Tool nur auf der Version des Nutzers: **3.10.6** mit Unity
2022.3.22f1 (Testpaket `a9d3ce4` läuft dort laut Nutzer, 13.11.). Das ist
dieselbe Version, aus der die Angaben in dieser Datei stammen.

## Befund: `VRCApi.GetAvatars` gibt es nicht

In keiner der geprüften Versionen. `SdkCompat.GetAvatarsMethod` findet daher
nichts, und **„☁ IDs" sowie der ▾-Auswahlknopf können mit keinem SDK seit 3.3.0
funktionieren** — sie enden mit „This SDK version has no VRCApi.GetAvatars".
Der Onboarding-Text (Schritt 4) und `PROJECT_STATE.md` verweisen seit dem
13.11. auf ▾; das ist bis zu einer Reparatur falsch.

Zwei Wege, die Liste zu holen:
- `VRCApi.Get<T>(string requestUrl, Dictionary<string, string> queryParams = null, bool forceRefresh = false, bool allowRetry = false, CancellationToken cancellationToken = default)`
  ist in 3.10.6 öffentlich; mit `T = List<VRCAvatar>` und dem Endpunkt
  `avatars` (Parameter `user=me`, `releaseStatus=all`, `n`, `offset`) käme
  direkt der Typ zurück, den das Tool schon verwendet. Über die Versionen noch
  nicht verglichen.
- `ApiAvatar.FetchList(…)` aus `VRCCore-Editor.dll` benutzt das SDK selbst im
  Content Manager, in allen geprüften Versionen. Callback-API, liefert
  `ApiAvatar` statt `VRCAvatar`.

## Multi-Platform des SDK ersetzt unsere Queue nicht

`BuildAndUploadMultiPlatform` nimmt die im SDK-Fenster gewählten Plattformen,
baut Windows zuerst, wechselt die Plattform und **kehrt beim Wechsel zurück**;
den Rest erledigt das SDK nach dem Domain Reload selbst. Für einen Avatar ist
das bequem. Für einen Batch würde es pro Outfit die Plattform wechseln. Unsere
Queue wechselt einmal pro Plattform für alle Outfits und bleibt deshalb die
bessere Lösung.

## Für die Upload-Zeit interessant

Bauen und Hochladen sind getrennt aufrufbar (`Build` liefert den Bundle-Pfad,
`VRCApi.UpdateAvatarBundle` lädt ihn hoch), und die Ereignisse oben erlauben,
Bau- und Upload-Dauer getrennt zu messen. Erst messen, wie sich die rund 2:36
pro Outfit aufteilen; davon hängt ab, ob sich ein Upload im Hintergrund während
des nächsten Builds lohnt. Ob das SDK das verträgt, ist ungeprüft.

## Entwurf für die eigene Assembly (noch nicht eingecheckt)

```json
{
  "name": "ShiroTools.OutfitBatchUploader.Editor",
  "rootNamespace": "ShiroTools",
  "references": ["VRC.SDKBase", "VRC.SDKBase.Editor", "VRC.SDK3A", "VRC.SDK3A.Editor"],
  "includePlatforms": ["Editor"],
  "overrideReferences": false,
  "autoReferenced": false
}
```

Paket: `com.shirotools.outfit-batch-uploader` (vom Nutzer bestätigt),
`vpmDependencies: { "com.vrchat.avatars": ">=3.8.0" }`, Unity 2022.3.

## So lässt es sich wiederholen

- Liste: `https://packages.vrchat.com/official?download`. Der Server verlangt
  einen User-Agent; ein generischer Client-Name genügt.
- Zips: `https://github.com/vrchat/packages/releases/download/<Version>/com.vrchat.<base|avatars>-<Version>.zip`,
  SHA-256 steht in der Liste (`zipSHA256`). Der Nutzer hat das Lesen von
  `vrchat/packages` am 13.11. ausdrücklich erlaubt.
- Die Zips nicht entpacken, nur lesen (`.asmdef`, `.cs`, `.dll.meta`).

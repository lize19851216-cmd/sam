# Plugin security policy

SAM loads plugins in-process, so plugins must be treated as trusted code. The loader enforces a default-deny execution policy: only DLLs whose full-file hash is listed in `trusted-plugins.sha256` in the plugin directory may execute in the desktop process.

To trust a reviewed plugin, calculate its hash and add the 64-character result as one line in the manifest:

```powershell
Get-FileHash .\MyPlugin.dll -Algorithm SHA256
```

Review the DLL before adding its hash. Any plugin update changes its hash and must be reviewed and added again. Unlisted DLLs are reported in the Plugin Center and are rejected before assembly loading.

The local named-pipe contract is metadata-only. It does not grant an untrusted plugin host services, and it is not an operating-system sandbox. A future out-of-process host must add OS-level restriction before untrusted plugin code can run.

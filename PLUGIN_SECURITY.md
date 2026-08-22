# Plugin security policy

SAM loads plugins in-process, so plugins must be treated as trusted code. The loader uses a default-deny SHA-256 allowlist: only DLLs whose full-file hash is listed in `trusted-plugins.sha256` in the plugin directory are loaded.

To trust a reviewed plugin, calculate its hash and add the 64-character result as one line in the manifest:

```powershell
Get-FileHash .\MyPlugin.dll -Algorithm SHA256
```

Review the DLL before adding its hash. Any plugin update changes its hash and must be reviewed and added again. Unlisted DLLs are reported in the Plugin Center and are never executed. This is a trusted-plugin policy, not a sandbox; process isolation for untrusted third-party plugins remains future work.

# Layout previews

Standalone Razor Pages for comparing three compact translator layouts. This project has no reference to the desktop application or translation engine. Both text areas are ordinary editable textareas; the swap buttons affect only local sample content.

Start from the repository root:

```powershell
dotnet run --project DesignTests/DesignTests.csproj --no-launch-profile --urls http://127.0.0.1:54831
```

Open http://127.0.0.1:54831. The gallery offers three layouts and a resizable preview frame. Each layout also opens directly at `/design/1`, `/design/2`, or `/design/3`. The Previews directory contains their 900-by-530 renderings.

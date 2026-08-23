# Rhino native package

`RhinoMCP.Plugin` is a Rhino 8 `.rhp` plug-in that loads at startup, exposes a
dockable status panel, and hosts the framed localhost bridge. `RhinoMCP.Grasshopper`
is a bundled `.gha` add-on that starts when Grasshopper opens; no canvas file or
script component is required.

Build both assemblies and a Package Manager `.yak` archive:

```sh
./scripts/build-yak.sh
```

The resulting package is written to `dist/` with both assemblies under `net7.0/`.

After the package owner has logged in to the Rhino package server, publish it with:

```sh
./scripts/publish-yak.sh
```

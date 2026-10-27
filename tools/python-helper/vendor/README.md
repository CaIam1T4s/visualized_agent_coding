# Bundled dependency

Parso 0.8.7, downloaded from the official PyPI wheel:
https://pypi.org/project/parso/0.8.7/

The `parso/` package and original `parso-0.8.7.dist-info/` metadata are
preserved without modifications. The upstream license is included at
`parso-0.8.7.dist-info/LICENSE.txt`.

This pure-Python dependency is copied with the helper during build and publish.
No global Python installation changes or runtime downloads are required.
Parsing explicitly disables both disk and incremental caches.

Reproduce the vendored package by downloading `parso==0.8.7` with
`pip download --no-deps --only-binary=:all:` and extracting the package
and its `.dist-info` directory from the wheel into this directory.

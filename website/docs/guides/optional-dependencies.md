---
id: optional-dependencies
slug: /guides/optional-dependencies
title: Optional dependencies
sidebar_label: Optional dependencies
description: Choose optional Unity package versions and preserve existing project dependencies.
---

# Optional dependencies

Open **Window → MCP for Unity → Dependencies**. ProBuilder, Cinemachine, VFX Graph, and glTFast each show their installed Package Manager version and a **Version (optional)** field.

## Choose a version

- Enter an exact version, such as `2.10.5` or `3.0.0-pre.1`, then click **Install**.
- Leave a missing package's field empty to let Unity select the latest compatible version.
- For an installed package, select a different version and click **Change Version**. You do not need to uninstall first.
- Leave an installed package's field empty, or keep its displayed version, to preserve the installation, including Git and local package sources.

Partial versions, ranges, `latest`, URLs, and package identifiers are rejected in this field. An inline message explains the required format, and installation stays disabled until the input is corrected. Unity Package Manager checks whether a valid exact version exists and is compatible with the project; its normal dependency resolution still applies.

Selections survive window rebuilds and package-triggered domain reloads within the current Editor session. They are not written to project settings. Dependencies loaded outside Package Manager remain managed at their original source.

## Install All

**Install All** uses the same version fields as individual installation. It requests missing packages and explicitly changed versions, and omits unchanged installed packages. All fields are validated before installation starts. The confirmation dialog lists the exact requests.

Roslyn uses its existing DLL installer and does not have a package-version field.

## Cinemachine compatibility

You can keep Cinemachine 2 installed. The native virtual-camera actions in `manage_camera` use the Cinemachine 3 camera API; basic Unity Camera operations remain available.

Check the installed version before writing package-specific code:

```python
manage_packages(action="get_package_info", package="com.unity.cinemachine")
manage_camera(action="ping")
```

`ping` reports the installed version and distinguishes installation from native API support. For Cinemachine 2 projects, use `execute_code` with that installed version's API. An unsupported native action explains this limitation without asking you to upgrade the project's package.

For automation, `manage_packages(action="add_package", package="com.unity.cinemachine@2.10.5")` also accepts exact versions. See [Package tools](../reference/tools/core/manage_packages.md).

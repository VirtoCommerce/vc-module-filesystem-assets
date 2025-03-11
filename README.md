# Virto Commerce File System Assets Module
[![CI status](https://github.com/VirtoCommerce/vc-module-filesystem-assets/workflows/Module%20CI/badge.svg?branch=dev)](https://github.com/VirtoCommerce/vc-module-filesystem-assets/actions?query=workflow%3A"Module+CI") [![Quality gate](https://sonarcloud.io/api/project_badges/measure?project=VirtoCommerce_vc-module-filesystem-assets&metric=alert_status&branch=dev)](https://sonarcloud.io/dashboard?id=VirtoCommerce_vc-module-filesystem-assets) [![Reliability rating](https://sonarcloud.io/api/project_badges/measure?project=VirtoCommerce_vc-module-filesystem-assets&metric=reliability_rating&branch=dev)](https://sonarcloud.io/dashboard?id=VirtoCommerce_vc-module-filesystem-assets) [![Security rating](https://sonarcloud.io/api/project_badges/measure?project=VirtoCommerce_vc-module-filesystem-assets&metric=security_rating&branch=dev)](https://sonarcloud.io/dashboard?id=VirtoCommerce_vc-module-filesystem-assets) [![Sqale rating](https://sonarcloud.io/api/project_badges/measure?project=VirtoCommerce_vc-module-filesystem-assets&metric=sqale_rating&branch=dev)](https://sonarcloud.io/dashboard?id=VirtoCommerce_vc-module-filesystem-assets)

The File System Assets module provides integration with File System.

## Settings
1. Open **appsettings.json** for the Virto Commerce Platform instance.
2. Navigate to the **Assets** node:
```json
    "Assets": {
        "Provider": "FileSystem",
        "FileSystem": {
            "RootPath": "~/assets",
            "PublicUrl": "http://localhost:10645/assets/"
        },
    }
```
3. Modify the following settings:
    - Set the **Provider** value to **FileSystem**.
    - Change **RootPath** to change root folder where the files are stored on the local disk.
    - Change **PublicUrl** based on your platform configuaration or CDN. This pass is used to create absolute asset url. 

## Documentation

* [File System Assets module user documentation](https://docs.virtocommerce.org/platform/user-guide/file-system/overview/)
* [Assets management](https://docs.virtocommerce.org/platform/user-guide/assets/managing-assets/)
* [Assets storage configuration](http://localhost/platform/developer-guide/Configuration-Reference/appsettingsjson/#assets)
* [REST API](https://virtostart-demo-admin.govirto.com/docs/index.html?urls.primaryName=VirtoCommerce.FileSystemAssets)
* [View on GitHub](https://github.com/VirtoCommerce/vc-module-filesystem-assets)

## References

* [Deployment](https://docs.virtocommerce.org/platform/developer-guide/Tutorials-and-How-tos/Tutorials/deploy-module-from-source-code/)
* [Installation](https://docs.virtocommerce.org/platform/user-guide/modules-installation/)
* [Home](https://virtocommerce.com)
* [Community](https://www.virtocommerce.org)
* [Download latest release](https://github.com/VirtoCommerce/vc-module-filesystem-assets/releases/latest)


## License

Copyright (c) Virto Solutions LTD.  All rights reserved.

Licensed under the Virto Commerce Open Software License (the "License"); you
may not use this file except in compliance with the License. You may
obtain a copy of the License at

http://virtocommerce.com/opensourcelicense

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
implied.


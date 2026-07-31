# Changelog

## [0.4.0](https://github.com/CitiesSkylinesModding/agents-plugins/compare/unity-devtools-mcp-v0.3.0...unity-devtools-mcp-v0.4.0) (2026-07-30)


### Features

* **unity-devtools:** add source-level debugging toolset ([26ca77e](https://github.com/CitiesSkylinesModding/agents-plugins/commit/26ca77e85a8456d355c96262f4abfcc109b9bb08))
* **unity-devtools:** answer what an entity carries in one call, kinds and enabled state included ([dca9b1d](https://github.com/CitiesSkylinesModding/agents-plugins/commit/dca9b1da2e6e7d9a3e89994d4c7100cb9d3cdee6))
* **unity-devtools:** find a type from a fragment, instead of leaving the session for a decompiler ([706b00c](https://github.com/CitiesSkylinesModding/agents-plugins/commit/706b00c0524e9f84f4b1fdc46242e8d005565994))
* **unity-devtools:** make every ECS tool read an entity the same way, and refuse what is not there ([2d0eaf8](https://github.com/CitiesSkylinesModding/agents-plugins/commit/2d0eaf880e928a06fad858ccbe031fd04528ec31))
* **unity-devtools:** reach state living on an entity's references, not on the entity itself ([cfedf39](https://github.com/CitiesSkylinesModding/agents-plugins/commit/cfedf39384e056bbe49c6fa39bc36aa0cb61c11b))
* **unity-devtools:** search a mod's types even when the game could not load them all ([aa3e5d8](https://github.com/CitiesSkylinesModding/agents-plugins/commit/aa3e5d8aa1db923a26a08ce15e05f06ec203cc33))
* **unity-devtools:** show what an entity's components hold, not just which ones it carries ([50b1058](https://github.com/CitiesSkylinesModding/agents-plugins/commit/50b1058cfa6491938891caf331c7d38ebd577323))


### Bug Fixes

* **unity-devtools-mcp:** guarantee the server dies with its client so MCP reconnects never strand a stale instance ([7afff80](https://github.com/CitiesSkylinesModding/agents-plugins/commit/7afff8024666f89cfb1ef9bc97e483016ddfe30e))
* **unity-devtools:** refuse a buffer the entity does not carry, instead of reading memory it does not own ([2adfba5](https://github.com/CitiesSkylinesModding/agents-plugins/commit/2adfba53f66c1121f5ff7c52f2c4847a22a136bb))
* **unity-devtools:** report the game's own exception on every tool, not only the evaluator's ([78fb50f](https://github.com/CitiesSkylinesModding/agents-plugins/commit/78fb50f47d40ba0a7255553f3e421126744341c7))

## [0.3.0](https://github.com/CitiesSkylinesModding/agents-plugins/compare/unity-devtools-mcp-v0.2.0...unity-devtools-mcp-v0.3.0) (2026-07-20)


### Features

* **unity-devtools:** rename NuGet package to UnityDevtools.Mcp and ship a package icon ([4f3c930](https://github.com/CitiesSkylinesModding/agents-plugins/commit/4f3c9301d00119e1a6266a8e81bac03dce6a8875))

## [0.2.0](https://github.com/CitiesSkylinesModding/agents-plugins/compare/unity-devtools-mcp-v0.1.0...unity-devtools-mcp-v0.2.0) (2026-07-20)


### Features

* **unity-devtools:** add C# expression evaluator and ship as a dnx dotnet tool ([5378a2c](https://github.com/CitiesSkylinesModding/agents-plugins/commit/5378a2ced0d5946a028b2983c1b2e4242ba03fa4))
* **unity-devtools:** add demo MCP server, extract SDB library, and C# formatting stack ([a143d8a](https://github.com/CitiesSkylinesModding/agents-plugins/commit/a143d8ac853b20be948b70d36b7960ecf6b95a12))
* **unity-devtools:** graduate from PoC to a full plugin ([b39ee7e](https://github.com/CitiesSkylinesModding/agents-plugins/commit/b39ee7ee3919d304b56d44f6429b3224d211bb11))
* **unity-devtools:** make status generic, drop the Cities2 default ([331bf7b](https://github.com/CitiesSkylinesModding/agents-plugins/commit/331bf7b8244f0fe81594a0ff7fa609c0ab94b17e))

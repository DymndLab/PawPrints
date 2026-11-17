# Engine and vehicle performance blocks

`PerformanceCapture` snapshots native propulsion and mass data on the game thread. `PerformanceData` computes display groups and values; `PerformancePainting` lays out the printed blocks. Rendering uses that snapshot without changing the live craft.

## Placement and formats

**Place > Part Specs** selects an engine; a part's context menu also offers **Add engine specs**. **Place > Vehicle Specs** adds a vehicle block. Blocks have movable headings, resize grips and context settings, with undo/redo support. Regeneration retains compatible blocks and rejects missing engine/stage identities without replacing the previous print.

**Compact table** uses text columns. **Stacked metrics** supports values inside bars, before bars or without bars. Individual metrics can override the style and maximum; zero maximum selects an automatic labelled range. Narrow layouts fall back to text when a bar cannot fit beside its value.

VAC and ASL columns can be selected independently. Engine fields include name, quantity, thrust, specific impulse, propellants, throttle range, gimbal, flow and dry mass. Vehicle fields add loaded mass, stored substance mass, TWR, burn duration, delta-v, dimensions, part count and substance loading. Text size, border, paper fill and calculation-reference notes are configurable.

Engine leaders share the label/flagnote ink renderer and endpoint styles. Routes avoid visible craft and other panels, use up to two bends and meet the block horizontally. If no clear route exists, the leader is omitted; moving the block can expose an approach. Print-wide styles update existing leaders; individual block overrides are also supported.

## Calculation basis

- Ratings use `RocketControllerData.ComputeFromCores` at full throttle, 0 Pa vacuum and 101325 Pa ASL. They describe nominal installed hardware, independent of current throttle and engine enable state. Print poses do not recalculate propulsion geometry.
- Identical single-controller engine types can be grouped. Quantity counts structural engine parts. Per-engine mode retains one assembly's ratings; combined mode sums the group. Multi-controller assemblies stay together but are not automatically grouped across assemblies. Dry mass is unavailable when that boundary cannot be separated reliably.
- Vehicle scope covers all installed engines, a native stage's initial active engine set, or a custom controller set. Installed/custom scopes use whole-vehicle mass; stage scope uses KSA's stage mass. Installed/custom totals do not establish that all selected engines can run together with the available plumbing.
- Vehicle thrust is the magnitude of the summed nominal thrust vectors. Effective Isp is that thrust divided by total mass flow and Earth standard gravity (9.80665 m/s^2). TWR uses the same gravity and scope mass. Engine/group blocks show scalar rated thrust totals.
- Whole-vehicle dry and stored mass come from KSA mass properties. Stored substances can include non-engine consumables. Stage fuel mass/loading use the native staged estimate. Whole-vehicle loading lists captured mole stores by substance; it is not a complete tank-capacity or solid-grain inventory.
- Burn duration and delta-v use KSA's cached staged fuel-feed calculation. Its environment is printed separately from the VAC/ASL rating columns. Whole-plan duration sums powered phases and excludes coasts. Custom groups have no fuel-feed estimate. Dirty or missing native estimates display unavailable until a later capture supplies them.
- Bars show numerical ranges in printed units. Automatic engine ranges compare captured engine ratings and round upward; other metrics use a rounded range containing the value. Fixed maxima support comparisons across prints.

## Gimbal diagrams

Both gimbal axes come from the main nozzle: the nozzle with the highest positive finite nominal full-throttle vacuum thrust. Each limit is read from that nozzle's parent gimbal assembly. A fixed main nozzle reports zero even if an auxiliary nozzle moves.

Stacked blocks draw the actual angle about a dashed neutral axis beside the throttle/gimbal text. Different local Y/Z limits have separate diagrams. Groups with differing limits display **Varies by engine**. Diagram size changes to fit the block; the represented angle does not.

## Verification

`dotnet run --project tests/Blueprint.Tests.csproj -c Release -- --performance` checks grouping, calculations, fields, layout, bar ranges, gimbal axes, leader routing, collisions and history. `python tools/acceptance.py --performance` compares native ratings and exercises blocks on Lander 1, Gemini7 and Rocket in an isolated game profile. See [DEVELOPMENT.md](../DEVELOPMENT.md) for prerequisites and fixture setup.

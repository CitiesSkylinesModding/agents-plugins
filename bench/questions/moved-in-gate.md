# The household state a code-created household is missing

## Prompt

A Cities: Skylines II mod creates a household entity from code, fills it with citizen entities, and gives it a `PropertyRenter` pointing at a residential building — without routing anyone through the game's travel systems. The household's citizens are never counted in the city population, their happiness never updates, and they never apply to school. Name the single piece of household state that is missing, name the only simulation system that writes it, and state the exact condition under which that write happens.

## Verified answer

The missing state is **`HouseholdFlags.MovedIn`**, a flag on the `Household` component:

- `src/Game/Game.Citizens/HouseholdFlags.cs:11` — `	MovedIn = 4`
- `src/Game/Game.Citizens/Household.cs:8` — `	public HouseholdFlags m_Flags;`

**One simulation system writes it: `CitizenTravelPurposeSystem`**, in the `ArriveType.Resident` arm of `ArriveJob`:

- `src/Game/Game.Simulation/CitizenTravelPurposeSystem.cs:354` — `					case ArriveType.Resident:`
- `:368` — `						value.m_Flags |= HouseholdFlags.MovedIn;`
- `:369` — `							m_Households[household] = value;`

**The guard is the whole of this line**, at `:357`:

`						if (m_PropertyRenters.HasComponent(household) && m_PropertyRenters[household].m_Property == present.m_Target)`

That is: the arriving citizen's household must carry a `PropertyRenter` whose `m_Property` is the very building being arrived at. The nearby `HasBuffer(household) && (value.m_Flags & HouseholdFlags.MovedIn) == 0` test at `:360` gates only the `CitizensMovedIn` statistics event, not the flag write. The arrival itself is enqueued by the same system at `:156` when a citizen with `Purpose.GoingHome` has its `Arrived` enabled.

Nothing anywhere in the decompile ever clears the flag — there is no `&= ~HouseholdFlags.MovedIn`. The only other write of the value at all is `Household.Deserialize` at `Game.Citizens/Household.cs:55`, restoring the whole flags byte from a save.

The consequences named in the prompt all check out, though the census link is two-hop: `ApplyValidCitizenJob` stamps or clears `CitizenFlags.ValidCitizen` from the flag (`CountHouseholdDataSystem.cs:315`, `:323`, `:339`) and `CountCitizensJob` then filters on `ValidCitizen` (`:540`), so the flag's reach extends to everything reading that. Happiness skips the citizen at `CitizenHappinessSystem.cs:294` with a `CitizenFlags.Tourist` carve-out; school application gates at `ApplyToSchoolSystem.cs:127` through `CitizenUtils.HasMovedIn`; and the pathfind money weight is multiplied by 0.1 at `CitizenUtils.cs:87` until the flag is set.

So a mod creating a household from code must either drive a real `GoingHome` arrival at the rented property or set the flag itself.

## Rubric

- 4: Names `HouseholdFlags.MovedIn` on the `Household` component as the missing state.
- 3: Names `CitizenTravelPurposeSystem` as the only simulation system that sets it, on a citizen arrival of type `ArriveType.Resident`.
- 3: Gives the condition: the household must have a `PropertyRenter` whose property is the building the citizen is arriving at.

## Roots

- decompile

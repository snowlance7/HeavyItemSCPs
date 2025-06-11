## 1.4.0
- Added SCP-513
- Added expensive SCP-178-1 body parts you can find only visible with SCP-178
- Removed noise detection for SCP-427-1
- SCP-427-1 no longer spawns from SCP-427 when it is on the ground
- SCP-323 no longer blurs vision when insanity increases, it is now tied to fear instead
- SCP-178 no longer uses WearableItemsAPI and now works the same as Comedy and Tragedy masks
- Changed rarity/spawn values for all SCPs
- Added a lot of performance fixes for SCP-178-1s
- A lot of bug fixes

## 1.3.2
- SCP-427-1 will force the player to drop all items when throwing them (configurable)
- SCP-427-1 has a cooldown for throwing players, when colliding with a player and still in cooldown, he will do a normal swipe attack that does 10 damage
- SCP-427 transform time has been changed from 120 to 45 seconds
- SCP-427 transform time only goes up when opening the locket
- SCP-427 transform time will slowly decrease by one second every 2.5 seconds when not holding SCP-427
- Fixed bug with player not dying when transforming into SCP-323-1
- Fixed bug with SCP-427-1 getting stuck when first spawning in

## 1.3.1
- Updated README to include SCP-323-1
- Added bestiaries for SCP-323-1 and SCP-178-1
- Changed link to SCP Discord page to an invite
- SCP-323-1 takes damage from explosions
- Fixed bug with SCP-323 not transforming client players and causing bugs
- SCP-178 can allow you to see scrap through walls if you are looking directly at it
- SCP-178-1 will only open doors when chasing
- SCP-178-1 will return to its spawn position if chasing the player too far
- Added percentage based spawning amount for SCP-178-1
- SCP-178-1 will only add anger when colliding with a player if they were being observed first

## 1.3.0
- Added SCP-323
- Added fear for SCP-178 while staring at SCP-178-1 instances
- Fixed spawning issues with SCP-178-1
- Better line of sight detection for SCP-178-1

## 1.2.1
- Players now drop all held items they were carrying when being picked up by SCP-427-1 (including the Cave Dweller baby) (configurable)
- Decreased priority of SCP-178-1's navmesh agent so they dont collide with other enemies
- Decreased SCP-427-1's player push force and distance as well as size limit so they dont get stuck in doorways and block players
- Only one instance of SCP-178 can and should exist at a time

## 1.2.0
- Added variations for SCP-427-1 depending on what he transforms from
- SCP-427-1 now throws hoarder bugs and baboon hawks
- Baboon hawks and RadMechs now see SCP-427-1 as a threat
- Fixed issue with player body not despawning properly when transforming
- Fixed issue with hoarderbugs not transforming
- More configs added for SCP-427-1 and SCP-178-1
- Optimizations for SCP-178-1
- SCP-427-1 now has 150 hp and can take damage from anything now
- SCP-427-1 now takes 75 damage from explosions

## 1.1.1
- Fixed issue with SCP-178-1 able to be scanned by other clients not wearing the glasses
- Fixed desyncing and softlocking issues when client other than host picks up and drops SCP-427
- Fixed issue with baboon hawks not transforming or picking up 427

## 1.1.0
- Added SCP-178
- Fixed bug with network handler when leaving a lobby and re-entering
- Fixed bug where client transforming will softlock them
- Fixed bug where 427-1 freezes
- Fixed bug where player softlocks after being thrown in some cases
- Fixed bug where player cant move and screen shakes after being thrown
- Fixed desync issues with 427

## 1.0.3
- Some fixes to how SCP-427-1 throws players
- AI Changes
- Some fixes to how the necklace opens and closes

## 1.0.2
- Added a new configs
- Added Secret Labs level rarity and SCP Dungeon rarity
- Heal amount for 427 changed from 10 to 5 holding and 15 to 10 when open
- Fixed some bugs for 427
- SCP-427-1 now properly spawns when turning spawning on

## 1.0.1
- SCP-427-1 now only takes damage from mines if chasing a player
- Fixed issue with players not transforming
- Fixed some issues with the necklace
- Player will no longer be critically injured after thrown and will lose half their stamina instead
- Fixed death animation

## 1.0.0
- Initial release adding SCP-427
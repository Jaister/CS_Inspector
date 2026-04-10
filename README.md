# CS_Inspector
CS2 Scripts for a CS2 private inspect server in Linux

#Testing commands:
//REDLINE AK
!g 7 282 361 0.002

//BLUE PHP
!g 60 1017 361 0.01 

//OCULTIST
!gl 5030 1417 500 0.01 

//KARA DOPP BP 
!g 507 417 500 0.01

//PANDORA
!gl 5030 10037 619 0.15

//Hedge
!gen 5030 10038 619 0.0001

//Gungnir
!gen 9 756 661 0.01

//BlueGEM (legacy mesh is auto-detected via config)
!gen 7 44 661 0.01 5 5 5 5

//Kara blue gem 
!gen 507 44 387 0.01

# Legacy mesh auto-detection

The plugin now checks `legacy_paints.json` and decides automatically whether a weapon should use the legacy bodygroup.

Config location (resolved from the server game directory base):
- `game/csgo/addons/counterstrikesharp/plugins/SkinInspect/legacy_paints.json`

Config format:

```json
{
	"legacy_by_weapon": {
		"7": [44, 282],
		"3": [265],
		"60": [1017]
	}
}
```

Notes:
- Keys are weapon defindexes as strings.
- Values are paint kit IDs that should render with legacy mesh for that weapon.
- Knives and gloves are ignored by this mapping.
- If a weapon/paint pair is not listed, modern mesh is used by default.
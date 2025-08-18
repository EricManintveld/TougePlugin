# Touge Plugin backport for assetto-server 0.0.54

A plugin for Assetto Corsa servers for **touge** racing with an elo system. Designed for competitive head-to-head driving with a configurable format. Find some live example servers running the plugin [here](https://assetto.scratchedclan.nl/servers).

---

## Features

- **Touge Format**: Race head-to-head in alternating lead/chase roles.
- **Various Rulesets**: Based on KaidoBattleNet rulesets.
- **Fully Configurable**: Tweak rules and behavior through a generated config file.

---
## Installation

- **Download the plugin**  
    _(Download link will be added here soon)_
- **Extract it**  
    Place the contents into your server's `plugins` directory.
- **Run your server once**  
    This will generate configuration files inside the `cfg` folder.
- **Customize your ruleset**  
    Edit the generated `plugin_touge_cfg.yml` and `touge_course_setup.ini` to adjust setting to your liking.

---

## Configuration


## Configuration
Enable the plugin in `extra_cfg.yml`
```yaml
EnablePlugins:
- TougePlugin
```

### `touge_course_setup.yml`
Used to setup the starting areas on various maps. You can use [comfy map](https://www.overtake.gg/downloads/comfy-map.52623/) to get the position and heading. The following example sets up two starting areas, one for Gunma and one for Imola. Configurations for different tracks can all be stored in the same file. A download link for a base configuration file that contains various starting positions for popular tracks will be added later. Also, feel free to share your configs in the [Discord](https://discord.gg/z22Pcsy3df).
```yaml
Tracks:
  shuto_revival_project_beta-main_layout:
    Courses:
      C1 Dash:
        FinishLine:
          - [-110.96, 16.55, -9157.65]
          - [-118.57, 16.63, -9161.25]
        StartingSlots:
          - Leader:
              Position: [2986.3,13,-9128.1]
              Heading: -50
            Follower:
              Position: [2982.4,13,-9121.2]
              Heading: -53
      Yaesu Route:
        FinishLine:
          - [1348.07, 15.12, -5881.17]
          - [1336.85, 15.01, -5880.16]
        StartingSlots:
          - Leader:
              Position: [2316.4,13.1,-8107.3]
              Heading: 1
            Follower:
              Position: [2322.7,13.1,-8104]
              Heading: 10
  pk_gunma_cycle_sports_center-gcsc_full_attack:
    Courses:
      Gunsai Loop:
        FinishLine:
          - [-110.96, 16.55, -9157.65]
          - [-118.57, 16.63, -9161.25]
        StartingSlots:
          - Leader:
              Position: [-207.5,467.7,-95.9]
              Heading: -18
            Follower:
              Position: [-201.5,467.4,-89.3]
              Heading: -18
          - Leader:
              Position: [-207.5,467.7,-95.9]
              Heading: -18
            Follower:
              Position: [-201.5,467.4,-89.3]
              Heading: 23
```

Example configuration (add to bottom of `extra_cfg.yml`)
```yaml
---
!TougeConfiguration
# Car performance ratings keyed by car model name. Can range from 1 - 1000.
CarPerformanceRatings:
  ks_mazda_miata: 125
  ks_toyota_ae86: 131
# Maximum elo gain. Must be a positive value.
MaxEloGain: 32
# Number of races for which is player is marked as provisional for the elo system.
ProvisionalRaces: 20
# Maximum elo gain, when player is marked as provisional
MaxEloGainProvisional: 50
# Rolling start enabled.
IsRollingStart: false
# Whether to use the track's built-in finish line as the course endpoint.
UseTrackFinish: true
# Outrun timer in seconds. Chase car has to finish within this amount of time after the lead car crosses the finish line.
CourseOutrunTime: 1.5
# Defines the ruleset used for touge sessions.
RuleSetType: CatAndMouse
# Whether players are allowed to challenge others to outrun races.
EnableOutrunRace: true
# Whether players are allowed to challenge others to defined-finish-line (course) races.
EnableCourseRace: true
# The number of seconds the chase car has to cross the finish line after the lead car finishes.
CourseOutrunTime: 30
# Time limit in seconds for outrun races. If the leader stays ahead for this duration, they win.
OutrunLeadTimeout: 120
# Distance in meters the lead car must maintain to win in an outrun race.
OutrunLeadDistance: 750
# Local database mode enabled. If disabled please provide database connection information.
IsDbLocalMode: true
# Connection string to PostgreSQL database. Can be left empty if isDbLocalMode = true.
PostgresqlConnectionString:
# Toggles discrete mode for the HUD. In discrete mode, the HUD only appears when necessary.
DiscreteMode: false
# Your Steam API key. Not required. 
SteamAPIKey: 
```

---

### Elo Configuration

#### `CarPerformanceRatings`
**Type:** `Dictionary<string, int>`  
**Description:** Specifies performance ratings for different car models.  
**Usage:** Each key represents the car's internal model name (e.g., `ks_mazda_miata`) and the value is a performance score between **1** and **1000**.  
**Purpose:** Used in player elo calculations to improve fairness. Winning in a faster car against a slower car will award less elo gain than beating a fast car using a slower one.  
**Example:**
```yaml
CarPerformanceRatings:
  ks_mazda_miata: 125
  ks_toyota_ae86: 131
```

#### `MaxEloGain`
**Type:** `int`  
**Description:** The maximum amount of elo rating a player can gain (or lose) in a single race.  
**Constraints:** Must be a **positive integer**.  
**Purpose:** Gives control over the volatility of the rating system.

#### `ProvisionalRaces`
**Type:** `int`  
**Description:** The number of initial races a player is considered "provisional" in the elo system.  
**Constraints:** Must be **greater than 0**.  
**Purpose:** Allows for slightly larger elo changes than configured in MaxEloGain while a player's skill is still being established.

#### `MaxEloGainProvisional`
**Type:** `int`  
**Description:** The maximum elo gain/loss while a player is still provisional.  
**Constraints:** Must be **greater than 0**.  
**Purpose:** Allows for a faster elo adjustment during provisional matches compared to regular ones.

---

### Race Setup

#### `IsRollingStart`
**Type:** `bool`  
**Description:** Enables or disables rolling starts.  
**Usage:**  
- `true`: Cars start moving at the beginning of the race.  
- `false`: Cars are stationary at the start.

#### `UseTrackFinish`
**Type:** `bool`  
**Description:** Whether to use the track's built-in finish line as the course endpoint.  
**Usage:**  
- `true`: Track finish line is used.  
- `false`: Custom finish defined in `touge_course_setup.yml` is used instead.

#### `RuleSetType`
**Type:** `string`  
**Options:** `BattleStage`, `CatAndMouse`  
**Description:** Defines the ruleset used for touge sessions. Find a more detailed description of the different rulesets [here](https://github.com/EricManintveld/TougePlugin/wiki/Rulesets-Explained).  
**Purpose:** Allows switching between different gameplay styles.  
**Constraints:** Must be one of the specified options.

#### `EnableOutrunRace`
**Type:** `bool`  
**Description:** Whether players are allowed to challenge others to outrun races.  
**Usage:**  
- `true`: Outrun races are available.  
- `false`: Outrun races are disabled.

#### `EnableCourseRace`
**Type:** `bool`  
**Description:** Whether players are allowed to challenge others to defined-finish-line (course) races.  
**Usage:**  
- `true`: Course races are available.  
- `false`: Course races are disabled.

#### `CourseOutrunTime`
**Type:** `int`  
**Description:** The number of seconds the **chase car** has to cross the finish line after the **lead car** finishes.  
**Constraints:** Must be between **1 and 60 seconds, and can contain a decimal point.** So, 1.5 is a valid value.  
**Purpose:** Used to determine if the lead car successfully outran the chase car.

#### `OutrunLeadTimeout`
**Type:** `int`  
**Description:** Time limit in seconds for outrun races. If the leader stays ahead for this duration, they win.  
**Constraints:** Must be **greater than 0**.

#### `OutrunLeadDistance`
**Type:** `int`  
**Description:** Distance in meters the lead car must maintain to win in an outrun race.  
**Constraints:** Must be **greater than 0**.

---

### Database

#### `IsDbLocalMode`
**Type:** `bool`  
**Description:** Whether the system should use a local in-memory or file-based database instead of a PostgreSQL server.  
**Usage:**
- `true`: No external DB needed; local data only.
- `false`: Requires valid PostgreSQL connection string.

#### `PostgresqlConnectionString`
**Type:** `string?`  
**Description:** Connection string used to connect to a PostgreSQL database.  
**Constraints:**  
- Must be non-empty **only if** `isDbLocalMode` is `false`.  
**Purpose:** Provides data persistence and multi-server support in non-local setups.

**Example:**
```yaml
postgresqlConnectionString: "Host={IP/URL};Port={Port};Username={Username};Password={Password};Database={Database name}"
```

---

### HUD

#### `DiscreteMode`
**Type:** `bool`  
**Description:** Toggles discrete mode for the HUD. In discrete mode, the HUD only appears when necessary.  
**Usage:**  
- `true`: HUD is shown contextually.  
- `false`: HUD is always visible.

#### `SteamAPIKey`
**Type:** `string`  
**Description:** Your Steam API key. **Not required**. You can find your Steam API key [here](https://steamcommunity.com/dev/apikey).  
**Constraints:** **Keep this value private**, never share it publicly.  
**Purpose:** Used for for loading player avatars through Steam.

---

## Notes

- Requires Content Manager with recent version of CSP enabled.
- This is a AssettoServer plugin.
	- Tested on [v0.0.55-pre26](https://github.com/compujuckel/AssettoServer/releases/tag/v0.0.55-pre26)
- UI and other plugin features may evolve — stay tuned for updates.

  ## Planned features
  - More rulesets.
  - Improved lap/course validation to prevent cheating.


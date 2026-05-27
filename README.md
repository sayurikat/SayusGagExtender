# Sayu's Gag Extender

A Dalamud plugin for Final Fantasy XIV that extends **GagSpeak** with extra automation, enforcement, controller tools, and quality-of-life features.

Sayu's Gag Extender watches GagSpeak state such as active restraints, restrictions, gags, blindfolds, and chat restrictions, then applies related effects through Dalamud hooks, in-game commands, and optional plugin integrations.

GagSpeak is required. Other supported plugins are optional and only needed for the features that use them.

---

## Installation

1. Open Dalamud Settings.
2. Go to the **Experimental** tab.
3. Add this custom plugin repository:

   ```text
   https://raw.githubusercontent.com/sayurikat/PartyPulse/refs/heads/master/repo/pluginmaster.json
   ```

4. Open the Dalamud Plugin Installer.
5. Search for **Sayu's Gag Extender**.
6. Install the plugin.

---

## Important warning

This plugin depends on Dalamud hooks and IPC/reflection-style integrations with other plugins.

Game updates, Dalamud updates, GagSpeak updates, or updates to supported plugins may break features until Sayu's Gag Extender is updated.

It is recommended to disable Sayu's Gag Extender before disabling, updating, reinstalling, or testing major changes to GagSpeak, Moodles, Penumbra, Customize+, Chat 2, XIVMessenger, Honorific, Cammy, or other supported integrations.

Blocking and enforcement features are best-effort quality-of-life tools. They are not security boundaries.

---

## Features overview

Sayu's Gag Extender currently includes:

- `/sge` commands for applying and removing GagSpeak restrictions, gags, and restraint sets.
- Emote Guard for more reliable remote emotes.
- Hand Guard for hands-bound style restrictions.
- Moodle-based blocking for teleporting, mounting, and job changes.
- Quotas for mount, teleport, and job-change usage.
- Fatigue tracking with forced walking, stopping, sitting.
- Job Roulette with whitelisted gearsets.
- Moodle, Penumbra, Customize+, Emote, Honorific, and Cammy enforcers.
- Random shock and vibration command automation.
- Chat 2 and XIVMessenger bridges for GagSpeak chat restrictions.
- GagSpeak Mirror for syncing restraint state from a main character to alts.
- Controller commands over configured chat channels.
- Controller interface for managing other configured users.
- Puppeteer Alias viewer for controller.

---

### Controller Interface

The Controller Interface is for users who control one or more other GagSpeak users.

Send remote commands, view active settings, control quotas, roulette, Honorific titles, and view Puppeteer aliases.

---

## Core features

### Emote Guard

Emote Guard improves reliability when emotes are triggered remotely, such as through GagSpeak Puppeteer.

It can queue emotes, wait for safer conditions, dismount when needed, wait through blocked states, and briefly prevent movement or action interruption so the emote has a better chance to start.


### Hand Guard

Hand Guard supports hands-bound style restrictions.

When configured GagSpeak restrictions are active, it attempts to keep weapons sheathed and suppress auto-attacks.

---

### Blocks

The Blocks tab lets Moodles control extra restrictions:

- Teleport Block
- Mount Block
- Job Switch Block

When any configured Moodle is active, the matching action is blocked. Mount Block can also attempt to dismount the player if they are already mounted.

### Quotas

The Quotas tab adds usage limits for:

- Mounting
- Teleporting
- Job changes

Each quota can be configured per hour or per day. Optional Moodles can show whether a quota is available or empty.

Remote controller commands can also set and lock these limits.

### Fatigue

Fatigue tracks movement and builds fatigue based on movement speed and configured active GagSpeak restrictions.

Fatigue can:

- Increase faster under configured restraints.
- Recover while standing or resting.
- Force walk at a configured threshold.
- Force stop at a configured threshold.
- Force sit at a configured threshold.
- Apply Moodles for enabled, restrained, and fatigue-status states.
- Apply Honorific titles for fatigue states.
- Show live status in the Main and Mini windows.


### Job Roulette

Job Roulette randomly switches between configured whitelisted gearsets on a schedule.

It can:

- Use only selected gearsets.
- Lock manual job changes while active.
- Spend job-change quota.
- Swap even while locked or out of quota, depending on settings.
- Be controlled remotely.
- Apply a Moodle and/or Honorific while roulette is active.

---

## Enforcers

Enforcers link GagSpeak state to other plugin or game states.

Most enforcers can be linked to:

- Restraint sets
- Restrictions
- Gags

### Moodle Enforcer

Keeps selected Moodles active while linked GagSpeak restraints are active, then removes them when the linked state ends.

### Penumbra Enforcer

Keeps selected Penumbra mods enabled while linked GagSpeak restraints are active.

This is intended for restraint-specific visual mods. Duplicate mods may be useful if the same mod should also be usable outside the enforcer.

### Customize+ Enforcer

Keeps selected Customize+ profiles enabled while linked GagSpeak restraints are active.

It also supports a default Customize+ profile to restore when no linked profile should be active.

### Emote Enforcer

Keeps a selected emote active while linked GagSpeak restraints are active.

While an emote is enforced, movement and most actions are blocked to prevent accidentally breaking out of the pose. A configurable cancel command is used when enforcement ends.

### Honorific Enforcer

Applies Honorific titles while linked GagSpeak restraints are active.

Titles support color, glow, priority, and cloned source JSON so hidden Honorific design data can be inlcuded.

### Cammy Enforcer

Applies Cammy presets while linked GagSpeak restraints are active.

The highest-priority matching preset wins. A default Cammy preset can be restored when enforcement ends.

---

## Automation

### Shock Collar

Shock Collar can send random configured shock commands while selected GagSpeak restrictions are active.

It supports weighted command lists, a configured controller name, controller-online checking, per-hour counts, remote locks, optional Moodles for active/controller-online state, and honorific title change while shock collar triggers.

### Vibrator

Vibrator works similarly to Shock Collar, but sends configured vibration commands.

It supports weighted command lists, controller-online checking, per-hour counts, remote locks, optional Moodles for active/controller-online state, and honorific title change while vibrator triggers.

---

## Chat integrations

### Chat 2

The Chat 2 bridge follows GagSpeak chat restrictions.

It can:

- Disable Chat 2 input when GagSpeak disables or hides chat input.
- Switch Chat 2 to a configured hidden/empty tab when GagSpeak hides chat.
- Save and apply a blindfold-friendly Chat 2 position.
- Optionally lock Chat 2 position while blindfolded.

### XIVMessenger

The XIVMessenger bridge follows GagSpeak chat restrictions.

It can:

- Keep XIVMessenger closed while GagSpeak hides chat.
- Disable XIVMessenger text input while GagSpeak hides or disables chat input.
- Restore text input after restrictions are removed.

---

## GagSpeak Mirror

GagSpeak Mirror saves the active GagSpeak state from a configured main character and mirrors it to alt characters.

It can mirror:

- Active restraint set
- Active restrictions
- Active gags

Locked mirroring can prevent alt characters from changing away from the saved restraint state.

For best results, alt characters should share the same GagSpeak wardrobe library as the main character.

Back up your GagSpeak configuration before changing profile folders.

GagSpeak usually stores configuration under:

```text
%AppData%\XIVLauncher\pluginConfigs\ProjectGagSpeak
```

A common setup is to replace an alt profile folder with a directory link to the main character's profile folder:

```text
mklink /D "%AppData%\XIVLauncher\pluginConfigs\ProjectGagSpeak\{AltProfileId}" "%AppData%\XIVLauncher\pluginConfigs\ProjectGagSpeak\{MainProfileId}"
```

Close the game and launcher before changing these folders.

---

## Puppeteer Aliases

The Puppeteer Aliases tab can read aliases from GagSpeak Puppeteer.

It can:

- Show alias folder, name, trigger.
- Store local notes for aliases.
- Allows Controller Interface to retrieve users alias library.

---

## Local commands

The base command is:

```text
/sge
```

Running `/sge` without a recognized subcommand opens the main window.

### Help

```text
/sge help
```

### GagSpeak restrictions

```text
/sge apply restriction [name]
/sge remove restriction [name]
```

### Gags

```text
/sge apply gag [name]
/sge remove gag [name]
```

### Restraint sets

```text
/sge apply restraintset [name]
/sge remove restraintset [name]
```

---

## Remote controller commands

Remote controller commands are configured in the Controller tab.

The controlled user must enable controller commands, configure the controller name/world, and select accepted chat channels.

Commands use the prefix:

```text
sge
```

Hidden/package commands may use the hidden prefix form used internally by the controller interface.

Available remote commands include:

```text
sge help
sge status
sge autozap [always/distant/offline]
sge zapcount [count]
sge zapcount unlock
sge autovibe [always/distant/offline]
sge vibecount [count]
sge vibecount unlock
sge mountlimit [day/hour] [count]
sge mountlimit unlimited
sge teleportlimit [day/hour] [count]
sge teleportlimit unlimited
sge joblimit [day/hour] [count]
sge joblimit unlimited
sge jobroulette [minutes]
sge stopjobroulette
sge settitle [title]
sge settemptitle [seconds] [title]
sge cleartitle
```

The Controller Interface wraps many of these commands in buttons and cached status fields.

---

## Integrations

| Integration | Required | Used for |
| --- | --- | --- |
| GagSpeak | Yes | Core restraint, gag, restriction, blindfold, chat, Puppeteer, and command state |
| Moodles | Optional | Moodles, blockers, quotas, fatigue effects, roulette effects |
| Penumbra | Optional | Restraint-linked mod enforcement |
| Customize+ | Optional | Restraint-linked profile enforcement and default profile restore |
| Chat 2 | Optional | Chat input/tab control and blindfold-friendly positioning |
| XIVMessenger | Optional | Chat restriction bridge |
| Honorific | Optional | Restraint, fatigue, roulette, and remote title effects |
| Cammy | Optional | Restraint-linked camera preset enforcement |

Features that depend on optional integrations do nothing unless the related plugin is installed, available, and configured.


---

## Notes and limitations

- GagSpeak is required for most features.
- Optional integrations must be installed and loaded for their related features to work.
- Blocks, quotas, forced walking, forced stopping, and forced sitting are best-effort.
- Remote commands can intentionally lock some local settings until the controller changes or unlocks them.
- There is no safeword or emergency override built into the remote controller feature.
- Updating or disabling supported plugins while Sayu's Gag Extender is running may cause errors.
- Make backups before linking GagSpeak profile folders for mirroring.


---

## Disclaimer

Sayu's Gag Extender is an unofficial plugin for use with Dalamud and Final Fantasy XIV.

It is not affiliated with or endorsed by Square Enix, Dalamud, GagSpeak, or the authors of any optional integration plugins.

---

## Support / Contact

Questions, bug reports, and feedback are welcome.

Discord: `sayurikat`

When reporting an issue, please include:

- Which feature you were using.
- What you expected to happen.
- What actually happened.
- Relevant Dalamud log messages.


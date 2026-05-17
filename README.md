## Installation

1. Open Dalamud Settings.
2. Go to the **Experimental** tab.
3. Add this custom plugin repository:

   ```text
   https://raw.githubusercontent.com/sayurikat/PartyPulse/refs/heads/master/repo/pluginmaster.json

4. Open the Dalamud Plugin Installer.
5. Search for Sayu's Gag Extender.
6. Install the plugin.


# Sayu's Gag Extender

A Dalamud plugin for Final Fantasy XIV that extends the functionality of **GagSpeak** with extra automation, enforcement, and quality-of-life tools.

Sayu's Gag Extender watches GagSpeak state changes such as active gags, restrictions, restraint sets, blindfold state, and chat restrictions, then reacts by applying related effects through other plugins and in-game systems. It can enforce emotes, block actions, manage Chat 2 behavior, mirror GagSpeak state across characters, and trigger configured commands while certain restrictions are active.

> This plugin is intended to be used together with GagSpeak. Several features also support optional plugin integrations such as Moodles, Penumbra, Customize+, and Chat 2.
>
> **Warning:** GagSpeak is required. Other supported plugins are optional, but disabling or updating them while Sayu's Gag Extender is running may cause errors because this plugin continuously monitors their state. It is recommended to disable Sayu's Gag Extender before disabling, updating, or reinstalling any supported plugin. Game updates, Dalamud updates, and updates to GagSpeak or any supported plugin may also break functionality until Sayu's Gag Extender is updated.

---

## Features

Sayu's Gag Extender expands GagSpeak with extra commands, additional restraint-style restrictions, random command automation, and integrations with other appearance/status plugins.

At a high level, it can:

- Add `/sge` commands for quickly applying or removing GagSpeak restraints by name.
- Add extra restriction behavior, such as blocking teleporting, mounting, and job switching when configured states are active.
- Trigger random shock or vibration commands while selected restraints are active.
- Monitor GagSpeak restraint state and use it to control other plugin states, such as Moodles, Penumbra mods, and Customize+ profiles.

See [Commands](#commands) for the full command list.

---

### Emote Guard

Emote Guard makes emotes more reliable when they are triggered remotely, such as when a GagSpeak controller uses Puppeteer to make your character perform an emote.

Normally, those emotes can fail or be ignored if your character is not ready to perform them. Emote Guard intercepts emotes, queues them, and waits until they can safely be carried out.

It helps ensure emotes still happen when your character is:

- Mounted
- Mounting or dismounting
- Moving
- Jumping
- Casting
- In combat
- Occupied by an event, cutscene, trade, crafting, gathering, fishing, or performance state
- Changing areas

When needed, Emote Guard can dismount, wait through loading screens or blocked states, briefly suppress combat actions, and wait for your character to stop moving before replaying the emote.

After the emote starts, movement and actions are blocked for a brief moment. This helps make sure the emote actually begins, since the player may not know in advance when their controller has issued it.

---

### Hand Guard

Hand Guard is intended to enhance GagSpeak's hand-blocking behavior for selected restraints.

When a configured restraint is active, Hand Guard attempts to keep your character from using their hands in combat by blocking auto-attacks and sheathing drawn weapons.

This helps enforce hands-bound or weapon-forbidden states more reliably.

---

### Teleport Block

Teleport Block is a restriction feature added by Sayu's Gag Extender.

When enabled, it prevents Teleport and Return while a configured Moodle is active. That Moodle should be activated by the relevant GagSpeak restraints, either through GagSpeak's own triggers or through Sayu's Gag Extender's Moodle Enforcer.

---

### Mount Block

Mount Block is a restriction feature added by Sayu's Gag Extender.

When enabled, it prevents mounting while a configured Moodle is active. That Moodle should be activated by the relevant GagSpeak restraints, either through GagSpeak's own triggers or through Sayu's Gag Extender's Moodle Enforcer.

If the player is already mounted when the block becomes active, the plugin will attempt to dismount them.

---

### Job Switch Block

Job Switch Block is a restriction feature added by Sayu's Gag Extender.

When enabled, it prevents changing jobs while a configured Moodle is active. That Moodle should be activated by the relevant GagSpeak restraints, either through GagSpeak's own triggers or through Sayu's Gag Extender's Moodle Enforcer.

If a blocked job switch is detected, the plugin will attempt to return the player to the previously allowed job.

---

### Moodle Enforcer

Moodle Enforcer keeps selected Moodle statuses active while linked GagSpeak restraints are active.

A Moodle can be linked to specific restraints, including restraint sets, restrictions, and gags. When any linked restraint is active, the selected Moodle is applied. When the linked restraints are no longer active, the Moodle is removed again.

This is useful for keeping status effects in sync with GagSpeak restraint states.

---

### Penumbra Enforcer

Penumbra Enforcer keeps selected Penumbra mods enabled while linked GagSpeak restraints are active.

A Penumbra mod can be linked to specific restraints, including restraint sets, restrictions, and gags. When any linked restraint is active, the selected mod is enabled on the player collection. When the linked restraints are no longer active, the mod is disabled again.

This is useful for making visual restraint mods stay enabled only while the matching GagSpeak restraint state is active.

---

### Customize+ Enforcer

Customize+ Enforcer keeps selected Customize+ profiles active while linked GagSpeak restraints are active.

A Customize+ profile can be linked to specific restraints, including restraint sets, restrictions, and gags. When any linked restraint is active, the selected profile is enabled. When the linked restraints are no longer active, the profile is disabled again.

This can be used to improve gag expressions, restore the correct facial expression when gags are removed, or apply other Customize+ changes for specific restraint states.

---

### Emote Enforcer

Emote Enforcer keeps selected emotes active while linked GagSpeak restraints are active.

An emote can be linked to specific restraints, including restraint sets, restrictions, and gags. When any linked restraint is active, the selected emote is enforced. When the linked restraints are no longer active, the enforced emote is cancelled.

While an emote is enforced, the player is locked in place and most actions and movement are blocked to prevent breaking out of the emote state. Expression emotes can still be used.

This is intended for restriction effects that depend on the player staying in a specific emote or pose.

---

### Shock Collar

Shock Collar can take over surprise shock commands while the player's controller is offline.

When selected restraints are active, such as shock collars or similar restraint items, Sayu's Gag Extender can send configured shock commands at random times. If the configured controller is online, the plugin skips the automated shock and leaves control to them instead.

Shock Collar works together with Emote Guard so the player can be dismounted, stopped, and held briefly when needed to help the shock command go through properly.

---

### Vibrator

Vibrator can take over surprise vibration commands while the player's controller is offline.

When selected restraints are active, such as vibrators or similar restraint items, Sayu's Gag Extender can send configured vibration commands at random times. If the configured controller is online, the plugin skips the automated vibration and leaves control to them instead.

Vibrator works together with Emote Guard so the player can be dismounted, stopped, and held briefly when needed to help the vibration command go through properly.

---

### Chat 2 Integration

Chat 2 Integration bridges the gap between GagSpeak's chat restrictions and Chat 2.

When GagSpeak blocks the normal in-game chat window or chat input field, Sayu's Gag Extender can apply similar restrictions to Chat 2. This includes disabling Chat 2 input when GagSpeak disables chat input.

GagSpeak cannot directly hide the Chat 2 window through this plugin. Instead, Sayu's Gag Extender can force Chat 2 onto a configured empty or unused tab while GagSpeak requests the chatbox to be hidden. In practice, this can serve the same purpose by preventing the player from reading useful chat content.

Chat 2 Integration can also help with blindfold behavior. Since blindfolds cover most of the screen, Chat 2 can be moved to a saved position in the remaining visible area so the player can still speak while blinded.

For a stronger blackout effect, Chat 2 can be forcefully locked over that visible area, preventing the player from seeing the game through the opening while blindfolded.

---

### GagSpeak Mirror

GagSpeak Mirror saves the active GagSpeak restraints from a configured main character and mirrors them onto other characters.

When the current character is set as the main character, Sayu's Gag Extender saves restraint changes. When an alt character is loaded, the plugin can apply the saved restraint state after GagSpeak is ready.

For this to work correctly, alt characters need the same GagSpeak wardrobe library as the main character. GagSpeak stores its data in Dalamud's plugin configuration folder, usually:

`%AppData%\XIVLauncher\pluginConfigs\ProjectGagSpeak`

Inside that folder, GagSpeak creates profile folders with alphanumeric names. Back up the `ProjectGagSpeak` folder, then replace each alt profile folder with a directory link to the main character's profile folder.

Close the game and launcher before changing these folders.

Open the Windows Start menu, search for `cmd`, then right-click **Command Prompt** and choose **Run as administrator**.

Command format:

`mklink /D "%AppData%\XIVLauncher\pluginConfigs\ProjectGagSpeak\{AltProfileId}" "%AppData%\XIVLauncher\pluginConfigs\ProjectGagSpeak\{MainProfileId}"`

Replace `{AltProfileId}` and `{MainProfileId}` with the actual alphanumeric folder names from your `ProjectGagSpeak` folder.

Important note: the alt profile folder must not already exist when running `mklink /D`. Rename or move the alt folder first, after making a backup.

Linking the wrong folders can make profiles share the wrong data, so make a backup first.

---

## Commands

The base command is:

```text
/sge
```

Running `/sge` without a recognized subcommand opens the main plugin window.

### Help

```text
/sge help
```

Prints the available command list in chat.

### Restrictions

```text
/sge apply restriction [name]
/sge remove restriction [name]
```

Applies or removes a GagSpeak restriction by name.

Examples:

```text
/sge apply restriction Wrist Cuffs
/sge remove restriction Wrist Cuffs
```

### Gags

```text
/sge apply gag [name]
/sge remove gag [name]
```

Applies or removes a GagSpeak gag by name.

Examples:

```text
/sge apply gag Ball Gag
/sge remove gag Ball Gag
```

### Restraint sets

```text
/sge apply restraintset [name]
/sge remove restraintset [name]
```

Applies or removes a GagSpeak restraint set by name.

Examples:

```text
/sge apply restraintset Heavy Bondage
/sge remove restraintset Heavy Bondage
```

---

## Main window

The main window provides a compact overview of the plugin's current state.

From here, you can quickly see which features are enabled and whether they are currently active. This makes it easier to tell at a glance whether a restraint, blocker, enforcer, Chat 2 state, or automation feature is currently doing something.

The window also shows a few live status values, such as the configured shock and vibration controllers, blindfold state, and GagSpeak chat restriction state.

Use the **Settings** button in the main window to open the configuration UI.

---

## Integrations

Sayu's Gag Extender is built around GagSpeak and can also integrate with several other plugins.

| Integration | Required | Used for |
| --- | --- | --- |
| GagSpeak | Yes | Core source of restraints, blindfold state, and chat restriction state |
| Moodles | No | Status effects used by Moodle Enforcer and as triggers for teleport, mount, and job-switch blocking |
| Penumbra | No | Enabling or disabling configured mods based on GagSpeak restraints |
| Customize+ | No | Enabling or disabling configured profiles based on GagSpeak restraints |
| Chat 2 | No | Chat tab/input control and blindfold position handling |

Features that depend on optional integrations will do nothing unless the related plugin is installed, available, and configured.

---

## Notes and limitations

- This plugin uses Dalamud hooks and plugin IPC/reflection-style integrations. Game updates, Dalamud updates, and updates to GagSpeak or any supported plugin may also break functionality until Sayu's Gag Extender is updated.
- The plugin assumes GagSpeak is installed and ready for most major features.
- Blocking features are best-effort. They are designed to catch normal action paths and common edge cases, but they should not be treated as security boundaries.
- Moodles, Penumbra, Customize+, and Chat 2 integrations require their respective plugins to be installed and up to date.

---

## Disclaimer

Sayu's Gag Extender is an unofficial plugin for use with Dalamud and Final Fantasy XIV. It is not affiliated with or endorsed by Square Enix, Dalamud, GagSpeak, or the authors of any optional integration plugins.

# Death Screen UI and Button Implementation Analysis

## File Locations

### Main Death Screen Files:

1. **[GUIManager.cs](Assets/TopDownEngine/Common/Scripts/Managers/GUIManager.cs#L33-L206)** - Death screen reference and control
2. **[LevelManager.cs](Assets/TopDownEngine/Common/Scripts/Managers/LevelManager.cs#L385-L480)** - Death screen display logic and respawn handling
3. **[LevelSelector.cs](Assets/TopDownEngine/Common/Scripts/GUI/LevelSelector.cs#L63-L79)** - Button handler implementations

---

## Death Screen UI Implementation

### Location: GUIManager.cs

**[GUIManager.cs](Assets/TopDownEngine/Common/Scripts/Managers/GUIManager.cs#L33-L35)**

```csharp
/// the death screen
[Tooltip("the death screen")]
public GameObject DeathScreen;
```

**[GUIManager.cs](Assets/TopDownEngine/Common/Scripts/Managers/GUIManager.cs#L202-L209)**

```csharp
/// <summary>
/// Sets the death screen on or off.
/// </summary>
/// <param name="state">If set to <c>true</c>, sets the pause.</param>
public virtual void SetDeathScreen(bool state)
{
    if (DeathScreen != null)
    {
        DeathScreen.SetActive(state);
        EventSystem.current.sendNavigationEvents = state;
    }
}
```

### Death Screen Display Flow

**[LevelManager.cs](Assets/TopDownEngine/Common/Scripts/Managers/LevelManager.cs#L391-L405)**

```csharp
/// <summary>
/// Kills the player.
/// </summary>
public virtual void PlayerDead(Character playerCharacter)
{
    if (Players.Count < 2)
    {
        StartCoroutine (PlayerDeadCo ());
    }
}

/// <summary>
/// Triggers the death screen display after a short delay
/// </summary>
/// <returns></returns>
protected virtual IEnumerator PlayerDeadCo()
{
    yield return new WaitForSeconds(DelayBeforeDeathScreen);

    GUIManager.Instance.SetDeathScreen(true);
}
```

**Configuration** [LevelManager.cs](Assets/TopDownEngine/Common/Scripts/Managers/LevelManager.cs#L69-L72):

- `DelayBeforeDeathScreen` = 1 second (adjustable in inspector)

---

## Button Implementations

Both "RESTART LEVEL" and "RELOAD LEVEL" buttons are implemented in **[LevelSelector.cs](Assets/TopDownEngine/Common/Scripts/GUI/LevelSelector.cs)**

### 1. RESTART LEVEL Button

**[LevelSelector.cs](Assets/TopDownEngine/Common/Scripts/GUI/LevelSelector.cs#L63-L68)**

```csharp
/// <summary>
/// Restarts the current level, without reloading the whole scene
/// </summary>
public virtual void RestartLevel()
{
    if (GameManager.Instance.Paused)
    {
        TopDownEngineEvent.Trigger(TopDownEngineEventTypes.UnPause, null);
    }
    TopDownEngineEvent.Trigger(TopDownEngineEventTypes.RespawnStarted, null);
}
```

**What it does:**

- Triggers `RespawnStarted` event
- Does NOT reload the scene
- Soft respawn: keeps all scene objects loaded
- Player respawns at the last checkpoint
- Scene state remains unchanged

### 2. RELOAD LEVEL Button

**[LevelSelector.cs](Assets/TopDownEngine/Common/Scripts/GUI/LevelSelector.cs#L75-L79)**

```csharp
/// <summary>
/// Reloads the current level
/// </summary>
public virtual void ReloadLevel()
{
    // we trigger an unPause event for the GameManager (and potentially other classes)
    TopDownEngineEvent.Trigger(TopDownEngineEventTypes.UnPause, null);
    LoadScene(SceneManager.GetActiveScene().name);
}
```

**[LevelSelector.cs](Assets/TopDownEngine/Common/Scripts/GUI/LevelSelector.cs#L38-L54)**

```csharp
protected virtual void LoadScene(string newSceneName)
{
    if (DestroyPersistentCharacter)
    {
        GameManager.Instance.DestroyPersistentCharacter();
    }

    if (GameManager.Instance.Paused)
    {
        TopDownEngineEvent.Trigger(TopDownEngineEventTypes.UnPause, null);
    }

    if (DoNotUseLevelManager)
    {
        MMAdditiveSceneLoadingManager.LoadScene(newSceneName);
    }
    else
    {
        LevelManager.Instance.GotoLevel(newSceneName);
    }
}
```

**What it does:**

- Completely reloads the current scene
- Uses Unity's `SceneManager.LoadScene()`
- Resets all scene objects to their initial state
- All destroyed objects are recreated
- Scene state is completely reset

---

## Key Differences: Respawn vs Reload

| Feature               | RESTART LEVEL          | RELOAD LEVEL           |
| --------------------- | ---------------------- | ---------------------- |
| **Event Type**        | `RespawnStarted`       | `UnPause` + Scene Load |
| **Scene Reload**      | NO                     | YES                    |
| **Objects Preserved** | Yes (old state)        | No (reset)             |
| **Performance**       | Faster                 | Slower                 |
| **Player Position**   | Checkpoint/Spawn point | Initial spawn point    |
| **Level State**       | Maintained             | Reset to initial       |
| **Respawn Animation** | Fade in/out            | Full scene reload      |

---

## Respawn Process Details

**[LevelManager.cs](Assets/TopDownEngine/Common/Scripts/Managers/LevelManager.cs#L410-L481)**

When `RespawnStarted` event is triggered, LevelManager listens and calls `Respawn()`:

```csharp
protected virtual void Respawn()
{
    if (Players.Count < 2)
    {
        StartCoroutine(SoloModeRestart());
    }
}

protected virtual IEnumerator SoloModeRestart()
{
    // Lose a life if using lives system
    if (GameManager.Instance.MaximumLives > 0)
    {
        GameManager.Instance.LoseLife();
        // Check for game over
        if (GameManager.Instance.CurrentLives <= 0)
        {
            TopDownEngineEvent.Trigger(TopDownEngineEventTypes.GameOver, null);
            // Load game over scene if configured
        }
    }

    // Stop camera following
    MMCameraEvent.Trigger(MMCameraEventTypes.StopFollowing);

    // Fade to black
    MMFadeInEvent.Trigger(OutroFadeDuration, FadeCurve, FaderID, true, Players[0].transform.position);
    yield return new WaitForSeconds(OutroFadeDuration);

    // Wait before respawn
    yield return new WaitForSeconds(RespawnDelay); // 2 seconds default

    // Hide death screen
    GUIManager.Instance.SetPauseScreen(false);
    GUIManager.Instance.SetDeathScreen(false);

    // Fade back in
    MMFadeOutEvent.Trigger(OutroFadeDuration, FadeCurve, FaderID, true, Players[0].transform.position);

    // Respawn player at checkpoint
    if (CurrentCheckpoint == null)
    {
        CurrentCheckpoint = InitialSpawnPoint;
    }

    if (Players[0] == null)
    {
        InstantiatePlayableCharacters();
    }

    if (CurrentCheckpoint != null)
    {
        CurrentCheckpoint.SpawnPlayer(Players[0]);
    }

    // Reset points
    TopDownEnginePointEvent.Trigger(PointsMethods.Set, 0);

    // Trigger respawn complete event
    TopDownEngineEvent.Trigger(TopDownEngineEventTypes.RespawnComplete, Players[0]);
}
```

---

## Configuration in Inspector

In **LevelManager** component:

- `RespawnDelay` (default: 2 seconds) - Time between death and actual respawn
- `DelayBeforeDeathScreen` (default: 1 second) - Time before showing death screen after player dies
- `OutroFadeDuration` - Fade effect timing

---

## Event Types Used

- `PlayerDeath` - Triggered when player health reaches 0
- `RespawnStarted` - Triggered by restart button
- `RespawnComplete` - Triggered after respawn process finishes
- `UnPause` - Triggered before reloading scene
- `GameOver` - Triggered if out of lives

---

## Button Connection Summary

The buttons should be connected in the DeathScreen UI prefab as follows:

1. **"RESTART LEVEL" Button** → `LevelSelector.RestartLevel()`
   - Triggers soft respawn at checkpoint
2. **"RELOAD LEVEL" Button** → `LevelSelector.ReloadLevel()`
   - Reloads entire scene fresh

Both methods can be called from UI Button's `OnClick` event in the inspector.

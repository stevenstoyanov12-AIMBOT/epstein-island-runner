using UnityEngine;
// Outside scene: puts the character picked on the (separate) select scene onto the player.
public class ChosenApplier : MonoBehaviour { void Start() { if (!string.IsNullOrEmpty(CharacterSelect.Chosen)) CharacterSelect.ApplyToPlayer(CharacterSelect.Chosen); } }

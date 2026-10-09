using UnityEngine.InputSystem;

namespace Downstream.Input
{
    /// <summary>
    /// Builds the racing action map in code, mirroring the design doc's control table
    /// (controller first, full keyboard). Each local player gets their own copy restricted to
    /// their own device, so split-screen players never read each other's input. Rebinds are
    /// stored as override JSON (<see cref="InputActionAsset.SaveBindingOverridesAsJson"/>) per profile;
    /// the one-handed preset is just another override JSON.
    /// Kept in code for the skeleton; a designer-editable .inputactions asset can replace it later
    /// without changing <see cref="PlayerBoatInput"/>.
    /// </summary>
    public static class DownstreamControls
    {
        public const string RaceMap = "Race";

        public static InputActionAsset Create()
        {
            var asset = UnityEngine.ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "DownstreamControls";
            var race = asset.AddActionMap(RaceMap);

            var throttle = race.AddAction("Throttle", InputActionType.Value, expectedControlLayout: "Axis");
            throttle.AddBinding("<Gamepad>/rightTrigger");
            throttle.AddBinding("<Keyboard>/w");
            throttle.AddBinding("<Keyboard>/upArrow");

            var brake = race.AddAction("Brake", InputActionType.Value, expectedControlLayout: "Axis");
            brake.AddBinding("<Gamepad>/leftTrigger");
            brake.AddBinding("<Keyboard>/s");
            brake.AddBinding("<Keyboard>/downArrow");

            var steer = race.AddAction("Steer", InputActionType.Value, expectedControlLayout: "Axis");
            steer.AddBinding("<Gamepad>/leftStick/x");
            steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");

            // Air pitch shares the stick and W/S; the sim only reads it while airborne.
            var pitch = race.AddAction("Pitch", InputActionType.Value, expectedControlLayout: "Axis");
            pitch.AddBinding("<Gamepad>/leftStick/y");
            pitch.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/s").With("Positive", "<Keyboard>/w");

            var hop = race.AddAction("HopDrift", InputActionType.Button);
            hop.AddBinding("<Gamepad>/rightShoulder");
            hop.AddBinding("<Keyboard>/space");

            var item = race.AddAction("UseItem", InputActionType.Button);
            item.AddBinding("<Gamepad>/leftShoulder");
            item.AddBinding("<Keyboard>/e");

            var look = race.AddAction("LookBack", InputActionType.Button);
            look.AddBinding("<Gamepad>/buttonNorth");
            look.AddBinding("<Keyboard>/q");

            var trick = race.AddAction("Trick", InputActionType.Value, expectedControlLayout: "Vector2");
            trick.AddBinding("<Gamepad>/rightStick");
            trick.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");

            return asset;
        }
    }
}

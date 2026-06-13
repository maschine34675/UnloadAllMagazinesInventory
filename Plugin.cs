using BepInEx;
using BepInEx.Logging;
using UnloadAllMagazinesInventory.Patches;

namespace UnloadAllMagazinesInventory
{
    [BepInPlugin("com.maschine.UnloadAllMagazinesInventory", "UnloadAllMagazinesInventory", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            new UnloadAllMagazinesInventoryButtonPatch().Enable();
            Log.LogInfo("UnloadAllMagazinesInventory loaded.");
        }
    }
}

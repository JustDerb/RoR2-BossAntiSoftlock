using BepInEx;
using BepInEx.Configuration;
using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;

// Allow scanning for ConCommand, and other stuff for Risk of Rain 2
[assembly: HG.Reflection.SearchableAttribute.OptIn]
namespace BossAntiSoftlock
{
    [BepInPlugin(GUID, ModName, Version)]
    public class BossAntiSoftlock : BaseUnityPlugin
    {
        public const string GUID = "com.justinderby.bossantisoftlock";
        public const string ModName = "Boss Anti-Softlock";
        public const string Version = "1.0.5";

        public static Dictionary<CharacterBody, Vector3> SpawnPositions = new Dictionary<CharacterBody, Vector3>();
        public static ConfigFile Configuration;
        public static ConfigEntry<bool> ModHint;
        public static ConfigEntry<bool> ResetVoid;
        public static BossAntiSoftlock Instance;

        public void Awake()
        {
            Instance = SingletonHelper.Assign(Instance, this);
            Configuration = new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath, GUID + ".cfg"), true);
            ModHint = Configuration.Bind("General", "Show Hints", true, "Whether to send a reminder every time the teleporter event has started.");
            ResetVoid = Configuration.Bind("General", "Reset Voidtouched Monsters", true, "Whether to also reset voidtouched monsters.");

            Stage.onStageStartGlobal += _ => SpawnPositions.Clear();
            On.RoR2.Run.OnServerCharacterBodySpawned += TrackNewMonster;
            GlobalEventManager.onCharacterDeathGlobal += RemoveMonster;
            TeleporterInteraction.onTeleporterBeginChargingGlobal += SendModHint;

            On.RoR2.Console.RunCmd += HandleCommand;
        }

        public void Destroy()
        {
            Instance = null;

            Stage.onStageStartGlobal -= _ => SpawnPositions.Clear();
            On.RoR2.Run.OnServerCharacterBodySpawned -= TrackNewMonster;
            GlobalEventManager.onCharacterDeathGlobal -= RemoveMonster;
            TeleporterInteraction.onTeleporterBeginChargingGlobal -= SendModHint;

            On.RoR2.Console.RunCmd -= HandleCommand;

            SpawnPositions.Clear();
        }

        private void SendModChat(string message)
        {
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage
            {
                baseToken = $"<color=#93c47d>{ModName}:</color> {message}"
            });
        }

#pragma warning disable IDE0051 // Remove unused private members
        [ConCommand(commandName = "bas_reset_positions", flags = ConVarFlags.SenderMustBeServer, helpText = "")]
        private static void ForceResetPositions(ConCommandArgs _)
#pragma warning restore IDE0051 // Remove unused private members
        {
            if (!Instance) return;

            Debug.Log($"Resetting all monster positions...");
            Instance.ResetCharactersPositions(GetBosses());
        }

        private void HandleCommand(On.RoR2.Console.orig_RunCmd orig, RoR2.Console self, RoR2.Console.CmdSender sender, string concommandName, List<string> userArgs)
        {
            orig(self, sender, concommandName, userArgs);

            if (!concommandName.Equals("say", StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }

            if (userArgs.Count == 0)
            {
                return;
            }

            string command = userArgs[0];
            switch(command.ToLower())
            {
                case "/bossreset":
                case "/boss_reset":
                case "/resetboss":
                case "/resetbosses":
                case "/reset_boss":
                case "/reset_bosses":
                case "/br":
                case "/rb":
                    List<CharacterBody> bosses = GetBosses();
                    SendModChat($"Resetting monster positions... ({bosses.Count} monster{(bosses.Count == 1 ? "" : "s")})");
                    try
                    {
                        ResetCharactersPositions(bosses);
                    } catch (Exception e)
                    {
                        Debug.LogException(e);
                        SendModChat("Error resetting boss positions; check console for more info!");
                    }
                    break;
            }
        }

        private void SendModHint(TeleporterInteraction obj)
        {
            if (!ModHint.Value)
            {
                return;
            }
            SendModChat("Type '/bossreset' to reset monster positions.");
        }

        private void TrackNewMonster(On.RoR2.Run.orig_OnServerCharacterBodySpawned orig, Run self, CharacterBody body)
        {
            orig(self, body);

            if (body.gameObject == null || !body.isActiveAndEnabled || body.isPlayerControlled)
            {
                return;
            }
            SpawnPositions.Add(body, body.footPosition);
        }

        private static List<CharacterBody> GetBosses()
        {
            List<CharacterBody> ret = new List<CharacterBody>();
            foreach (CharacterBody body in SpawnPositions.Keys)
            {
                if (body?.gameObject == null || !body.isActiveAndEnabled)
                {
                    continue;
                }
                if (body.isBoss ||
                    // Track and return all monsters in Simulacrum
                    Run.instance is InfiniteTowerRun ||
                    // If configured, return void touched monsters too
                    (ResetVoid.Value && body.teamComponent.teamIndex == TeamIndex.Void))
                {
                    ret.Add(body);
                }
            }
            return ret;
        }

        private void RemoveMonster(DamageReport report)
        {
            if (report?.victimBody != null)
            {
                // We don't care to check anything else... if it's not present then that's fine
                // We just want to ensure this dictionary never grows
                SpawnPositions.Remove(report.victimBody);
            }
        }

        private void ResetCharactersPositions(List<CharacterBody> bodies)
        {
            if (ResetVoid.Value) foreach (var team in TeamComponent.GetTeamMembers(TeamIndex.Void)) if (team.body != null && !bodies.Contains(team.body)) bodies.Add(team.body);
            foreach (CharacterBody body in bodies)
            {
                if (body == null || body.gameObject == null || !body.isActiveAndEnabled)
                {
                    SpawnPositions.Remove(body); // caught inactive body
                    continue;
                }
                Debug.Log($"{GUID} - Teleporting {body} to {SpawnPositions[body]}");
                TeleportHelper.TeleportBody(body, SpawnPositions[body]);

                GameObject bodyObject = body.gameObject;
                if (bodyObject)
                {
                    foreach (EntityStateMachine entityStateMachine in bodyObject.GetComponents<EntityStateMachine>())
                    {
                        entityStateMachine.initialStateType = entityStateMachine.mainStateType;
                    }

                    // Add some effects
                    GameObject teleportEffectPrefab = Run.instance.GetTeleportEffectPrefab(bodyObject.gameObject);
                    if (teleportEffectPrefab)
                    {
                        EffectManager.SimpleEffect(teleportEffectPrefab, SpawnPositions[body], Quaternion.identity, true);
                    }
                }
            }
        }
    }
}

using BepInEx;
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
        public const string Version = "1.0.2";

        private GameObject TeleporterInstance;
        private Dictionary<CharacterMaster, Vector3> SpawnPositions;

        public static BossAntiSoftlock Instance;

        public void Awake()
        {
            Instance = SingletonHelper.Assign(Instance, this);
            SpawnPositions = new Dictionary<CharacterMaster, Vector3>();

            On.RoR2.Run.OnServerBossAdded += TrackNewBoss;
            GlobalEventManager.onCharacterDeathGlobal += RemoveBoss;
            TeleporterInteraction.onTeleporterBeginChargingGlobal += SendModHint;

            SceneDirector.onPostPopulateSceneServer += SceneDirector_onPostPopulateSceneServer;

            On.RoR2.Console.RunCmd += HandleCommand;
        }

        public void Destroy()
        {
            Instance = null;

            On.RoR2.Run.OnServerBossAdded -= TrackNewBoss;
            GlobalEventManager.onCharacterDeathGlobal -= RemoveBoss;
            TeleporterInteraction.onTeleporterBeginChargingGlobal -= SendModHint;

            SceneDirector.onPostPopulateSceneServer -= SceneDirector_onPostPopulateSceneServer;

            On.RoR2.Console.RunCmd -= HandleCommand;

            SpawnPositions.Clear();
            TeleporterInstance = null;
        }

        private void SceneDirector_onPostPopulateSceneServer(SceneDirector director)
        {
            TeleporterInstance = director.teleporterInstance;
        }

        private void SendModChat(string message)
        {
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage
            {
                baseToken = $"<color=#93c47d>{ModName}:</color> {message}"
            });
        }

        [ConCommand(commandName = "bas_reset_positions", flags = ConVarFlags.SenderMustBeServer, helpText = "")]
        private static void ForceResetPositions(ConCommandArgs _)
        {
            if (!Instance)
            {
                return;
            }

            Debug.Log($"Resetting all boss positions...");
            Instance.ResetCharactersPositions(Instance.GetAllBossCharacters());
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

            String command = userArgs[0];
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
                    List<CharacterMaster> characters = Instance.GetEligibleBossCharacters();
                    SendModChat($"Resetting boss positions... ({characters.Count} boss{(characters.Count == 1 ? "" : "es")})");
                    try
                    {
                        ResetCharactersPositions(characters);
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
            SendModChat("Type '/bossreset' to reset boss positions.");
        }

        private void TrackNewBoss(On.RoR2.Run.orig_OnServerBossAdded orig, Run self, BossGroup bossGroup, CharacterMaster characterMaster)
        {
            try
            {
                GameObject body = characterMaster.GetBodyObject();
                if (!body)
                {
                    Debug.LogError($"{GUID} - {characterMaster} does not have a body attached!");
                }
                SpawnPositions.Add(characterMaster, body.transform.position);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                orig(self, bossGroup, characterMaster);
            }
        }

        private void RemoveBoss(DamageReport report)
        {
            if (report?.victimMaster != null)
            {
                // We don't care to check anything else... if it's not present then that's fine
                // We just want to ensure this dictionary never grows
                SpawnPositions.Remove(report.victimMaster);
            }
        }

        private List<CharacterMaster> GetAllBossCharacters()
        {
            List<CharacterMaster> eligible = new List<CharacterMaster>();
            List<BossGroup> instancesList = InstanceTracker.GetInstancesList<BossGroup>();
            for (int i = 0; i < instancesList.Count; i++)
            {
                foreach (CharacterMaster character in instancesList[i].combatSquad.readOnlyMembersList)
                {
                    eligible.Add(character);
                }
            }

            return eligible;
        }

        private List<CharacterMaster> GetEligibleBossCharacters()
        {
            List<CharacterMaster> all = GetAllBossCharacters();
            List<CharacterMaster> eligible = new List<CharacterMaster>();

            foreach (CharacterMaster character in all)
            {
                // TODO: Implement this
                // Could be distance to player, or teleporter
                eligible.Add(character);
            }

            return eligible;
        }

        private void ResetCharactersPositions(List<CharacterMaster> characterMasters)
        {
            foreach(CharacterMaster characterMaster in characterMasters)
            {
                CharacterBody body = characterMaster.GetBody();
                if (body != null)
                {
                    if (!SpawnPositions.TryGetValue(characterMaster, out Vector3 spawnPoint))
                    {
                        Debug.LogError($"{GUID} - Got CharacterMaster that should have been in the spawn list!");
                        spawnPoint = Run.instance.FindSafeTeleportPosition(body, TeleporterInstance.transform);
                    }

                    Debug.Log($"{GUID} - Teleporting {body} to {spawnPoint}");
                    TeleportHelper.TeleportGameObject(body.gameObject, spawnPoint);

                    GameObject bodyObject = characterMaster.GetBodyObject();
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
                            EffectManager.SimpleEffect(teleportEffectPrefab, spawnPoint, Quaternion.identity, true);
                        }
                    }
                }
            }
        }
    }
}

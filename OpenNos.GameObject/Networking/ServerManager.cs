/*
 * This file is part of the OpenNos Emulator Project. See AUTHORS file for Copyright information
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 */

using OpenNos.Core;
using OpenNos.DAL;
using OpenNos.Data;
using OpenNos.Domain;
using OpenNos.GameObject.Helpers;
using OpenNos.Master.Library.Client;
using OpenNos.Master.Library.Data;
using OpenNos.XMLModel.Models.Quest;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace OpenNos.GameObject
{
    public class ServerManager : BroadcastableBase
    {
        #region Members

        public ThreadSafeSortedList<long, Group> GroupsThreadSafe;

        public bool InShutdown;

        public bool IsReboot { get; set; }

        public bool ShutdownStop;

        private static readonly ConcurrentBag<Card> _cards = new ConcurrentBag<Card>();

        private static readonly ConcurrentBag<Item> _items = new ConcurrentBag<Item>();

        private static readonly ConcurrentDictionary<Guid, MapInstance> _mapinstances = new ConcurrentDictionary<Guid, MapInstance>();

        private static readonly ConcurrentBag<Map> _maps = new ConcurrentBag<Map>();

        private static readonly ConcurrentBag<NpcMonster> _npcs = new ConcurrentBag<NpcMonster>();

        private static readonly CryptoRandom _random = new CryptoRandom();

        private static readonly int _seed = Environment.TickCount;

        private static readonly ConcurrentBag<Skill> _skills = new ConcurrentBag<Skill>();

        private static ServerManager _instance;

        private List<DropDTO> _generalDrops;

        private bool _inRelationRefreshMode;

        private long _lastGroupId;

        private ThreadSafeSortedList<short, List<MapNpc>> _mapNpcs;

        private ThreadSafeSortedList<short, List<DropDTO>> _monsterDrops;

        private ThreadSafeSortedList<short, List<NpcMonsterSkill>> _monsterSkills;

        private ThreadSafeSortedList<int, RecipeListDTO> _recipeLists;

        private ThreadSafeSortedList<short, Recipe> _recipes;

        private ThreadSafeSortedList<int, List<ShopItemDTO>> _shopItems;

        private ThreadSafeSortedList<int, Shop> _shops;

        private ThreadSafeSortedList<int, List<ShopSkillDTO>> _shopSkills;

        private ThreadSafeSortedList<int, List<TeleporterDTO>> _teleporters;

        #endregion

        #region Instantiation

        private ServerManager()
        {
            // do nothing
        }

        #endregion

        #region Properties

        public static ServerManager Instance => _instance ?? (_instance = new ServerManager());

        public Act4Stat Act4AngelStat { get; set; }

        public Act4Stat Act4DemonStat { get; set; }

        public DateTime Act4RaidStart { get; set; }

        public MapInstance ArenaInstance { get; private set; }

        public ThreadSafeGenericList<BazaarItemLink> BazaarList { get; set; }

        public int ChannelId { get; set; }

        public List<CharacterRelationDTO> CharacterRelations { get; set; }

        public ConfigurationObject Configuration { get; set; }

        public bool EventInWaiting { get; set; }

        public MapInstance FamilyArenaInstance { get; private set; }

        public ThreadSafeSortedList<long, Family> FamilyList { get; set; }

        public List<Group> GroupList { get; set; } = new List<Group>();

        public List<Group> Groups => GroupsThreadSafe.GetAllItems();

        public bool InBazaarRefreshMode { get; set; }

        public List<int> MateIds { get; internal set; } = new List<int>();

        public List<PenaltyLogDTO> PenaltyLogs { get; set; }

        public ThreadSafeSortedList<long, QuestModel> QuestList { get; set; }

        public ConcurrentBag<ScriptedInstance> Raids { get; set; }

        public List<Schedule> Schedules { get; set; }

        public string ServerGroup { get; set; }

        public List<EventType> StartedEvents { get; set; }

        public Task TaskShutdown { get; set; }

        public List<CharacterDTO> TopComplimented { get; set; }

        public List<CharacterDTO> TopPoints { get; set; }

        public List<CharacterDTO> TopReputation { get; set; }

        public Guid WorldId { get; private set; }

        #endregion

        #region Methods

        public void AddGroup(Group group) => GroupsThreadSafe[group.GroupId] = group;

        public void AskPVPRevive(long characterId)
        {
            ClientSession Session = GetSessionByCharacterId(characterId);
            if (Session?.HasSelectedCharacter == true)
            {
                if (Session.Character.IsVehicled)
                {
                    Session.Character.RemoveVehicle();
                }
                List<BuffType> bufftodisable = new List<BuffType>
                {
                    BuffType.Bad,
                    BuffType.Good,
                    BuffType.Neutral
                };
                Session.Character.DisableBuffs(bufftodisable);
                Session.SendPacket(Session.Character.GenerateStat());
                Session.SendPacket(Session.Character.GenerateCond());
                Session.SendPackets(UserInterfaceHelper.Instance.GenerateVb());

                Session.SendPacket("eff_ob -1 -1 0 4269");
                Session.SendPacket(UserInterfaceHelper.Instance.GenerateDialog($"#revival^2 #revival^1 {Language.Instance.GetMessageFromKey("ASK_REVIVE_PVP")}"));
                reviveTask(Session);
            }
        }

        // PacketHandler -> with Callback?
        public void AskRevive(long characterId)
        {
            ClientSession Session = GetSessionByCharacterId(characterId);
            if (Session?.HasSelectedCharacter == true && Session.HasCurrentMapInstance)
            {
                if (Session.Character.IsVehicled)
                {
                    Session.Character.RemoveVehicle();
                }
                List<BuffType> bufftodisable = new List<BuffType>
                {
                    BuffType.Bad,
                    BuffType.Good,
                    BuffType.Neutral
                };
                Session.Character.DisableBuffs(bufftodisable);
                Session.SendPacket(Session.Character.GenerateStat());
                Session.SendPacket(Session.Character.GenerateCond());
                Session.SendPackets(UserInterfaceHelper.Instance.GenerateVb());

                switch (Session.CurrentMapInstance.MapInstanceType)
                {
                    case MapInstanceType.BaseMapInstance:
                        if (ChannelId != 51)
                        {
                            if (Session.Character.Level > 20)
                            {
                                Session.Character.Dignity -= (short)(Session.Character.Level < 50 ? Session.Character.Level : 50);
                                if (Session.Character.Dignity < -1000)
                                {
                                    Session.Character.Dignity = -1000;
                                }
                                Session.SendPacket(Session.Character.GenerateSay(string.Format(Language.Instance.GetMessageFromKey("LOSE_DIGNITY"), (short)(Session.Character.Level < 50 ? Session.Character.Level : 50)), 11));
                                Session.SendPacket(Session.Character.GenerateFd());
                                Session.CurrentMapInstance?.Broadcast(Session, Session.Character.GenerateIn(), ReceiverType.AllExceptMe);
                                Session.CurrentMapInstance?.Broadcast(Session, Session.Character.GenerateGidx(), ReceiverType.AllExceptMe);
                            }
                            Session.SendPacket("eff_ob -1 -1 0 4269");
                            Session.SendPacket(UserInterfaceHelper.Instance.GenerateDialog($"#revival^0 #revival^1 {(Session.Character.Level > 20 ? Language.Instance.GetMessageFromKey("ASK_REVIVE") : Language.Instance.GetMessageFromKey("ASK_REVIVE_FREE"))}"));
                        }
                        else
                        {
                            Session.Character.IsVehicled = true;
                            Session.Character.Morph = 1564;
                            Session.CurrentMapInstance?.Broadcast(Session.Character.GenerateCMode());
                        }

                        reviveTask(Session);
                        break;

                    case MapInstanceType.TimeSpaceInstance:
                        if (!(Session.CurrentMapInstance.InstanceBag.Lives - Session.CurrentMapInstance.InstanceBag.DeadList.Count <= 1))
                        {
                            Session.Character.Hp = 1;
                            Session.Character.Mp = 1;
                            return;
                        }
                        Session.SendPacket(UserInterfaceHelper.Instance.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("YOU_HAVE_LIFE"), Session.CurrentMapInstance.InstanceBag.Lives - Session.CurrentMapInstance.InstanceBag.DeadList.Count + 1), 0));
                        Session.SendPacket(UserInterfaceHelper.Instance.GenerateDialog($"#revival^1 #revival^1 {(Session.Character.Level > 10 ? Language.Instance.GetMessageFromKey("ASK_REVIVE_TS_LOW_LEVEL") : Language.Instance.GetMessageFromKey("ASK_REVIVE_TS"))}"));
                        Session.CurrentMapInstance.InstanceBag.DeadList.Add(Session.Character.CharacterId);
                        reviveTask(Session);
                        break;

                    case MapInstanceType.RaidInstance:
                        List<long> save = Session.CurrentMapInstance.InstanceBag.DeadList.ConvertAll(s => s);
                        if (Session.CurrentMapInstance.InstanceBag.Lives - Session.CurrentMapInstance.InstanceBag.DeadList.Count < 0)
                        {
                            Session.Character.Hp = 1;
                            Session.Character.Mp = 1;
                        }
                        else if (2 - save.Count(s => s == Session.Character.CharacterId) > 0)
                        {
                            Session.SendPacket(UserInterfaceHelper.Instance.GenerateInfo(string.Format(Language.Instance.GetMessageFromKey("YOU_HAVE_LIFE_RAID"), 2 - Session.CurrentMapInstance.InstanceBag.DeadList.Count(s => s == Session.Character.CharacterId))));
                            Session.SendPacket(UserInterfaceHelper.Instance.GenerateInfo(string.Format(Language.Instance.GetMessageFromKey("RAID_MEMBER_DEAD"), Session.Character.Name)));
                            try
                            {
                                Session.Character.Group?.Raid?.InstanceBag.DeadList.Add(Session.Character.CharacterId);
                            }
                            catch (IndexOutOfRangeException ex)
                            {
                                Logger.Error(ex);
                            }
                            Session.Character.Group?.Characters.ForEach(session =>
                            {
                                session.SendPacket(session.Character.Group.GeneraterRaidmbf(session));
                                session.SendPacket(session.Character.Group.GenerateRdlst());
                            });
                            Task.Factory.StartNew(async () =>
                            {
                                await Task.Delay(20000).ConfigureAwait(false);
                                Instance.ReviveFirstPosition(Session.Character.CharacterId);
                            });
                        }
                        else
                        {
                            Group grp = Session.Character.Group;
                            if (grp != null)
                            {
                                grp.Characters.ForEach(s =>
                                {
                                    s.SendPacket(s.Character.Group.GeneraterRaidmbf(s));
                                    s.SendPacket(s.Character.Group.GenerateRdlst());
                                });
                                grp.LeaveGroup(Session);
                                Session.SendPacket(Session.Character.GenerateRaid(1, true));
                                Session.SendPacket(Session.Character.GenerateRaid(2, true));
                                Session.SendPacket(UserInterfaceHelper.Instance.GenerateMsg(Language.Instance.GetMessageFromKey("KICKED_FROM_RAID"), 0));
                            }
                        }
                        break;

                    case MapInstanceType.LodInstance:
                        Session.SendPacket(UserInterfaceHelper.Instance.GenerateDialog($"#revival^0 #revival^1 {Language.Instance.GetMessageFromKey("ASK_REVIVE_LOD")}"));
                        reviveTask(Session);
                        break;

                    default:
                        Instance.ReviveFirstPosition(Session.Character.CharacterId);
                        break;
                }
            }
        }

        public void BazaarRefresh(long BazaarItemId)
        {
            InBazaarRefreshMode = true;
            CommunicationServiceClient.Instance.UpdateBazaar(ServerGroup, BazaarItemId);
            SpinWait.SpinUntil(() => !InBazaarRefreshMode);
        }

        public void ChangeMap(long id, short? MapId = null, short? mapX = null, short? mapY = null)
        {
            ClientSession session = GetSessionByCharacterId(id);
            if (session?.Character != null)
            {
                if (MapId != null)
                {
                    session.Character.MapInstanceId = GetBaseMapInstanceIdByMapId((short)MapId);
                }
                ChangeMapInstance(id, session.Character.MapInstanceId, mapX, mapY);
            }
        }

        // Both partly
        public void ChangeMapInstance(long sessionId, Guid MapInstanceId, short? mapX = null, short? mapY = null)
        {
            ClientSession session = GetSessionByCharacterId(sessionId);
            if (session?.Character != null && !session.Character.IsChangingMapInstance)
            {
                try
                {
                    if (session.Character.IsExchanging)
                    {
                        session.Character.CloseExchangeOrTrade();
                    }
                    if (session.Character.HasShopOpened)
                    {
                        session.Character.CloseShop();
                    }

                    session.CurrentMapInstance.RemoveMonstersTarget(session.Character.CharacterId);
                    session.CurrentMapInstance.UnregisterSession(session.Character.CharacterId);

                    LeaveMap(session.Character.CharacterId);

                    session.Character.IsChangingMapInstance = true;

                    // cleanup sending queue to avoid sending uneccessary packets to it
                    session.ClearLowPriorityQueue();

                    session.Character.IsSitting = false;
                    session.Character.MapInstanceId = MapInstanceId;
                    if (session.Character.MapInstance.MapInstanceType == MapInstanceType.BaseMapInstance)
                    {
                        session.Character.MapId = session.Character.MapInstance.Map.MapId;
                        if (mapX != null && mapY != null)
                        {
                            session.Character.MapX = mapX.Value;
                            session.Character.MapY = mapY.Value;
                        }
                    }
                    if (mapX != null && mapY != null)
                    {
                        session.Character.PositionX = mapX.Value;
                        session.Character.PositionY = mapY.Value;
                        foreach (Mate mate in session.Character.Mates.Where(m => m.IsTeamMember))
                        {
                            mate.PositionX = (short)(mapX.Value + (mate.MateType == MateType.Partner ? -1 : 1));
                            mate.PositionY = (short)(mapY.Value + 1);
                        }
                    }

                    session.CurrentMapInstance = session.Character.MapInstance;
                    session.CurrentMapInstance.RegisterSession(session);

                    session.SendPacket(session.Character.GenerateCInfo());
                    session.SendPacket(session.Character.GenerateCMode());
                    session.SendPacket(session.Character.GenerateEq());
                    session.SendPacket(session.Character.GenerateEquipment());
                    session.SendPacket(session.Character.GenerateLev());
                    session.SendPacket(session.Character.GenerateStat());
                    session.SendPacket(session.Character.GenerateAt());
                    session.SendPacket(session.Character.GenerateCond());
                    session.SendPacket(session.Character.GenerateCMap());
                    session.SendPacket(session.Character.GenerateStatChar());
                    session.SendPacket(session.Character.GeneratePairy());
                    session.SendPacket(session.Character.GeneratePinit());
                    session.SendPackets(session.Character.GeneratePst());
                    session.SendPacket(session.Character.GenerateAct());
                    session.SendPacket(session.Character.GenerateScpStc());
                    if (ChannelId == 51)
                    {
                        session.SendPacket(session.Character.GenerateFc());

                        if (MapInstanceId == session.Character.Family?.Act4Raid?.MapInstanceId || MapInstanceId == session.Character.Family?.Act4RaidBossMap?.MapInstanceId)
                        {
                            session.SendPacket(session.Character.GenerateDG());
                        }
                    }
                    if (session.Character.Group?.Raid != null && session.Character.Group.Raid.InstanceBag?.Lock == true)
                    {
                        session.SendPacket(session.Character.Group.GeneraterRaidmbf(session));
                    }

                    Parallel.ForEach(session.CurrentMapInstance.Sessions.Where(s => s.Character?.InvisibleGm == false && s.Character.CharacterId != session.Character.CharacterId), visibleSession =>
                    {
                        if (ChannelId != 51 || session.Character.Faction == visibleSession.Character.Faction)
                        {
                            session.SendPacket(visibleSession.Character.GenerateIn());
                            session.SendPacket(visibleSession.Character.GenerateGidx());
                            visibleSession.Character.Mates.Where(m => m.IsTeamMember && m.CharacterId != session.Character.CharacterId).ToList().ForEach(m => session.SendPacket(m.GenerateIn()));
                        }
                        else
                        {
                            session.SendPacket(visibleSession.Character.GenerateIn(true));
                            visibleSession.Character.Mates.Where(m => m.IsTeamMember && m.CharacterId != session.Character.CharacterId).ToList().ForEach(m => session.SendPacket(m.GenerateIn(true)));
                        }
                    });

                    session.SendPackets(session.CurrentMapInstance.GetMapItems());
                    MapInstancePortalHandler.GenerateMinilandEntryPortals(session.CurrentMapInstance.Map.MapId, session.Character.Miniland.MapInstanceId).ForEach(p => session.SendPacket(p.GenerateGp()));

                    if (session.CurrentMapInstance.InstanceBag.Clock.Enabled)
                    {
                        session.SendPacket(session.CurrentMapInstance.InstanceBag.Clock.GetClock());
                    }
                    if (session.CurrentMapInstance.Clock.Enabled)
                    {
                        session.SendPacket(session.CurrentMapInstance.InstanceBag.Clock.GetClock());
                    }

                    // TODO: fix this
                    if (session.Character.MapInstance.Map.MapTypes.Any(m => m.MapTypeId == (short)MapTypeEnum.CleftOfDarkness))
                    {
                        session.SendPacket("bc 0 0 0");
                    }
                    if (!session.Character.InvisibleGm)
                    {
                        Parallel.ForEach(session.Character.Mates.Where(m => m.IsTeamMember), mate =>
                        {
                            mate.PositionX = (short)(session.Character.PositionX + (mate.MateType == MateType.Partner ? -1 : 1));
                            mate.PositionY = (short)(session.Character.PositionY + 1);
                        });
                        Parallel.ForEach(session.CurrentMapInstance.Sessions.Where(s => s.Character != null), s =>
                        {
                            if (ChannelId != 51 || session.Character.Faction == s.Character.Faction)
                            {
                                s.SendPacket(session.Character.GenerateIn());
                                s.SendPacket(session.Character.GenerateGidx());
                                session.Character.Mates.Where(m => m.IsTeamMember).ToList().ForEach(m => s.SendPacket(m.GenerateIn()));
                            }
                            else
                            {
                                s.SendPacket(session.Character.GenerateIn(true));
                                session.Character.Mates.Where(m => m.IsTeamMember).ToList().ForEach(m => s.SendPacket(m.GenerateIn(true)));
                            }
                        });
                    }
                    if (session.Character.Size != 10)
                    {
                        session.SendPacket(session.Character.GenerateScal());
                    }
                    if (session.CurrentMapInstance?.IsDancing == true && !session.Character.IsDancing)
                    {
                        session.CurrentMapInstance?.Broadcast("dance 2");
                    }
                    else if (session.CurrentMapInstance?.IsDancing == false && session.Character.IsDancing)
                    {
                        session.Character.IsDancing = false;
                        session.CurrentMapInstance?.Broadcast("dance");
                    }
                    if (Groups != null)
                    {
                        Parallel.ForEach(Groups, group =>
                        {
                            foreach (ClientSession groupSession in group.Characters.GetAllItems())
                            {
                                ClientSession groupCharacterSession = Sessions.FirstOrDefault(s => s.Character != null && s.Character.CharacterId == groupSession.Character.CharacterId && s.CurrentMapInstance == groupSession.CurrentMapInstance);
                                if (groupCharacterSession == null)
                                {
                                    continue;
                                }
                                groupSession.SendPacket(groupSession.Character.GeneratePinit());
                                groupSession.SendPackets(groupSession.Character.GeneratePst());
                            }
                        });
                    }

                    if (session.Character.Group?.GroupType == GroupType.Group)
                    {
                        session.CurrentMapInstance?.Broadcast(session, session.Character.GeneratePidx(), ReceiverType.AllExceptMe);
                    }

                    session.Character.IsChangingMapInstance = false;
                    session.SendPacket(session.Character.GenerateMinimapPosition());
                    session.CurrentMapInstance.OnCharacterDiscoveringMapEvents.ForEach(e =>
                    {
                        if (!e.Item2.Contains(session.Character.CharacterId))
                        {
                            e.Item2.Add(session.Character.CharacterId);
                            EventHelper.Instance.RunEvent(e.Item1, session);
                        }
                    });
                }
                catch (Exception)
                {
                    Logger.Warn("Character changed while changing map. Do not abuse Commands.");
                    session.Character.IsChangingMapInstance = false;
                }
            }
        }

        public void FamilyRefresh(long FamilyId) => CommunicationServiceClient.Instance.UpdateFamily(ServerGroup, FamilyId);

        public MapInstance GenerateMapInstance(short mapId, MapInstanceType type, InstanceBag mapclock)
        {
            Map map = _maps.FirstOrDefault(m => m.MapId.Equals(mapId));
            if (map != null)
            {
                Guid guid = Guid.NewGuid();
                MapInstance mapInstance = new MapInstance(map, guid, false, type, mapclock);
                mapInstance.LoadMonsters();
                mapInstance.LoadNpcs();
                mapInstance.LoadPortals();
                Parallel.ForEach(mapInstance.Monsters, mapMonster =>
                {
                    mapMonster.MapInstance = mapInstance;
                    mapInstance.AddMonster(mapMonster);
                });
                Parallel.ForEach(mapInstance.Npcs, mapNpc =>
                {
                    mapNpc.MapInstance = mapInstance;
                    mapInstance.AddNPC(mapNpc);
                });
                _mapinstances.TryAdd(guid, mapInstance);
                return mapInstance;
            }
            return null;
        }

        public IEnumerable<Card> GetAllCard() => _cards;

        public List<MapInstance> GetAllMapInstances() => _mapinstances.Values.ToList();

        public List<Recipe> GetAllRecipes() => _recipes.GetAllItems();

        public IEnumerable<Skill> GetAllSkill() => _skills;

        public Guid GetBaseMapInstanceIdByMapId(short MapId) => _mapinstances.FirstOrDefault(s => s.Value?.Map.MapId == MapId && s.Value.MapInstanceType == MapInstanceType.BaseMapInstance).Key;

        public Card GetCard(short cardId) => _cards.FirstOrDefault(m => m.CardId.Equals(cardId));

        public List<DropDTO> GetDropsByMonsterVNum(short monsterVNum) => _monsterDrops.ContainsKey(monsterVNum) ? _generalDrops.Concat(_monsterDrops[monsterVNum]).ToList() : new List<DropDTO>();

        public Group GetGroupByCharacterId(long characterId) => Groups?.SingleOrDefault(g => g.IsMemberOfGroup(characterId));

        public Item GetItem(short vnum) => _items.FirstOrDefault(m => m.VNum.Equals(vnum));

        public MapInstance GetMapInstance(Guid id) => _mapinstances.ContainsKey(id) ? _mapinstances[id] : null;

        public List<MapInstance> GetMapInstances() => _mapinstances.Values.ToList();

        public long GetNextGroupId() => ++_lastGroupId;

        public NpcMonster GetNpc(short npcVNum) => _npcs.FirstOrDefault(m => m.NpcMonsterVNum.Equals(npcVNum));

        public T GetProperty<T>(string charName, string property)
        {
            ClientSession session = Sessions.FirstOrDefault(s => s.Character?.Name.Equals(charName) == true);
            if (session == null)
            {
                return default;
            }
            return (T)session.Character.GetType().GetProperties().Single(pi => pi.Name == property).GetValue(session.Character, null);
        }

        public T GetProperty<T>(long charId, string property)
        {
            ClientSession session = GetSessionByCharacterId(charId);
            if (session == null)
            {
                return default;
            }
            return (T)session.Character.GetType().GetProperties().Single(pi => pi.Name == property).GetValue(session.Character, null);
        }

        public List<Recipe> GetRecipesByItemVNum(short itemVNum)
        {
            List<Recipe> recipes = new List<Recipe>();
            foreach (RecipeListDTO recipeList in _recipeLists.Where(r => r.ItemVNum == itemVNum))
            {
                recipes.Add(_recipes[recipeList.RecipeId]);
            }
            return recipes;
        }

        public List<Recipe> GetRecipesByMapNpcId(int mapNpcId)
        {
            List<Recipe> recipes = new List<Recipe>();
            foreach (RecipeListDTO recipeList in _recipeLists.Where(r => r.MapNpcId == mapNpcId))
            {
                recipes.Add(_recipes[recipeList.RecipeId]);
            }
            return recipes;
        }

        public ClientSession GetSessionByCharacterName(string name) => Sessions.SingleOrDefault(s => s.Character.Name == name);

        public ClientSession GetSessionBySessionId(int sessionId) => Sessions.SingleOrDefault(s => s.SessionId == sessionId);

        public Skill GetSkill(short skillVNum) => _skills.FirstOrDefault(m => m.SkillVNum.Equals(skillVNum));

        public T GetUserMethod<T>(long characterId, string methodName)
        {
            ClientSession session = GetSessionByCharacterId(characterId);
            if (session == null)
            {
                return default;
            }
            MethodInfo method = session.Character.GetType().GetMethod(methodName);

            return (T)method.Invoke(session.Character, null);
        }

        public void GroupLeave(ClientSession session)
        {
            if (Groups != null)
            {
                Group grp = Instance.Groups.Find(s => s.IsMemberOfGroup(session.Character.CharacterId));
                if (grp != null)
                {
                    switch (grp.GroupType)
                    {
                        case GroupType.BigTeam:
                        case GroupType.Team:
                            if (grp.Characters.ElementAt(0) == session && grp.CharacterCount > 1)
                            {
                                Broadcast(session, UserInterfaceHelper.Instance.GenerateInfo(Language.Instance.GetMessageFromKey("NEW_LEADER")), ReceiverType.OnlySomeone, string.Empty, grp.Characters.ElementAt(1).Character.CharacterId);
                            }
                            grp.LeaveGroup(session);
                            session.SendPacket(session.Character.GenerateRaid(1, true));
                            session.SendPacket(session.Character.GenerateRaid(2, true));
                            foreach (ClientSession groupSession in grp.Characters.GetAllItems())
                            {
                                groupSession.SendPacket(grp.GenerateRdlst());
                                groupSession.SendPacket(groupSession.Character.GenerateRaid(0));
                            }
                            if (session?.CurrentMapInstance?.MapInstanceType == MapInstanceType.RaidInstance)
                            {
                                ChangeMap(session.Character.CharacterId, session.Character.MapId, session.Character.MapX, session.Character.MapY);
                            }
                            session.SendPacket(UserInterfaceHelper.Instance.GenerateMsg(Language.Instance.GetMessageFromKey("RAID_LEFT"), 0));
                            break;

                        case GroupType.GiantTeam:
                            ClientSession[] grpmembers = new ClientSession[40];
                            grp.Characters.CopyTo(grpmembers);
                            foreach (ClientSession targetSession in grpmembers)
                            {
                                if (targetSession != null)
                                {
                                    targetSession.SendPacket(UserInterfaceHelper.Instance.GenerateMsg(Language.Instance.GetMessageFromKey("GROUP_CLOSED"), 0));
                                    Broadcast(targetSession.Character.GeneratePidx(true));
                                    grp.LeaveGroup(targetSession);
                                    targetSession.SendPacket(targetSession.Character.GeneratePinit());
                                    targetSession.SendPackets(targetSession.Character.GeneratePst());
                                }
                            }
                            GroupList.RemoveAll(s => s.GroupId == grp.GroupId);
                            GroupsThreadSafe.Remove(grp.GroupId);
                            break;

                        case GroupType.Group:
                            if (grp.Characters.ElementAt(0) == session && grp.CharacterCount > 1)
                            {
                                Broadcast(session, UserInterfaceHelper.Instance.GenerateInfo(Language.Instance.GetMessageFromKey("NEW_LEADER")), ReceiverType.OnlySomeone, string.Empty, grp.Characters.ElementAt(1).Character.CharacterId);
                            }
                            grp.LeaveGroup(session);
                            if (grp.CharacterCount == 1)
                            {
                                ClientSession targetSession = grp.Characters.ElementAt(0);
                                if (targetSession != null)
                                {
                                    targetSession.SendPacket(UserInterfaceHelper.Instance.GenerateMsg(Language.Instance.GetMessageFromKey("GROUP_CLOSED"), 0));
                                    Broadcast(targetSession.Character.GeneratePidx(true));
                                    grp.LeaveGroup(targetSession);
                                    targetSession.SendPacket(targetSession.Character.GeneratePinit());
                                    targetSession.SendPackets(targetSession.Character.GeneratePst());
                                }
                            }
                            else
                            {
                                foreach (ClientSession groupSession in grp.Characters.GetAllItems())
                                {
                                    groupSession.SendPacket(groupSession.Character.GeneratePinit());
                                    groupSession.SendPackets(session.Character.GeneratePst());
                                    groupSession.SendPacket(UserInterfaceHelper.Instance.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("LEAVE_GROUP"), session.Character.Name), 0));
                                }
                            }
                            session.SendPacket(session.Character.GeneratePinit());
                            session.SendPackets(session.Character.GeneratePst());
                            Broadcast(session.Character.GeneratePidx(true));
                            session.SendPacket(UserInterfaceHelper.Instance.GenerateMsg(Language.Instance.GetMessageFromKey("GROUP_LEFT"), 0));
                            break;

                        default:
                            return;
                    }
                    session.Character.Group = null;
                }
            }
        }

        public void Initialize()
        {
            Act4RaidStart = DateTime.Now;
            Act4AngelStat = new Act4Stat();
            Act4DemonStat = new Act4Stat();

            // Load Configuration         

            Schedules = ConfigurationManager.GetSection("eventScheduler") as List<Schedule>;

            OrderablePartitioner<ItemDTO> itemPartitioner = Partitioner.Create(DAOFactory.ItemDAO.LoadAll(), EnumerablePartitionerOptions.NoBuffering);
            Parallel.ForEach(itemPartitioner, new ParallelOptions { MaxDegreeOfParallelism = 4 }, itemDTO =>
            {
                switch (itemDTO.ItemType)
                {
                    case ItemType.Armor:
                    case ItemType.Jewelery:
                    case ItemType.Fashion:
                    case ItemType.Specialist:
                    case ItemType.Weapon:
                        _items.Add(new WearableItem(itemDTO));
                        break;

                    case ItemType.Box:
                        _items.Add(new BoxItem(itemDTO));
                        break;

                    case ItemType.Shell:
                    case ItemType.Magical:
                    case ItemType.Event:
                        _items.Add(new MagicalItem(itemDTO));
                        break;

                    case ItemType.Food:
                        _items.Add(new FoodItem(itemDTO));
                        break;

                    case ItemType.Potion:
                        _items.Add(new PotionItem(itemDTO));
                        break;

                    case ItemType.Production:
                        _items.Add(new ProduceItem(itemDTO));
                        break;

                    case ItemType.Snack:
                        _items.Add(new SnackItem(itemDTO));
                        break;

                    case ItemType.Special:
                        _items.Add(new SpecialItem(itemDTO));
                        break;

                    case ItemType.Teacher:
                        _items.Add(new TeacherItem(itemDTO));
                        break;

                    case ItemType.Upgrade:
                        _items.Add(new UpgradeItem(itemDTO));
                        break;

                    default:
                        _items.Add(new NoFunctionItem(itemDTO));
                        break;
                }
            });
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("ITEMS_LOADED"), _items.Count));

            // intialize monsterdrops
            _monsterDrops = new ThreadSafeSortedList<short, List<DropDTO>>();
            Parallel.ForEach(DAOFactory.DropDAO.LoadAll().GroupBy(d => d.MonsterVNum), monsterDropGrouping =>
            {
                if (monsterDropGrouping.Key.HasValue)
                {
                    _monsterDrops[monsterDropGrouping.Key.Value] = monsterDropGrouping.OrderBy(d => d.DropChance).ToList();
                }
                else
                {
                    _generalDrops = monsterDropGrouping.ToList();
                }
            });
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("DROPS_LOADED"), _monsterDrops.Sum(i => i.Count)));

            // initialize monsterskills
            _monsterSkills = new ThreadSafeSortedList<short, List<NpcMonsterSkill>>();
            Parallel.ForEach(DAOFactory.NpcMonsterSkillDAO.LoadAll().GroupBy(n => n.NpcMonsterVNum), monsterSkillGrouping => _monsterSkills[monsterSkillGrouping.Key] = monsterSkillGrouping.Select(n => n as NpcMonsterSkill).ToList());
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("MONSTERSKILLS_LOADED"), _monsterSkills.Sum(i => i.Count)));

            // initialize bazaar
            BazaarList = new ThreadSafeGenericList<BazaarItemLink>();
            OrderablePartitioner<BazaarItemDTO> bazaarPartitioner = Partitioner.Create(DAOFactory.BazaarItemDAO.LoadAll(), EnumerablePartitionerOptions.NoBuffering);
            Parallel.ForEach(bazaarPartitioner, new ParallelOptions { MaxDegreeOfParallelism = 8 }, bazaarItem =>
            {
                BazaarItemLink item = new BazaarItemLink
                {
                    BazaarItem = bazaarItem
                };
                CharacterDTO chara = DAOFactory.CharacterDAO.LoadById(bazaarItem.SellerId);
                if (chara != null)
                {
                    item.Owner = chara.Name;
                    item.Item = new ItemInstance(DAOFactory.IteminstanceDAO.LoadById(bazaarItem.ItemInstanceId));
                }
                BazaarList.Add(item);
            });
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("BAZAAR_LOADED"), BazaarList.Count));

            // initialize npcmonsters
            Parallel.ForEach(DAOFactory.NpcMonsterDAO.LoadAll(), npcMonster =>
            {
                NpcMonster npcMonsterObj = new NpcMonster(npcMonster);
                npcMonsterObj.Initialize();
                npcMonsterObj.BCards = new List<BCard>();
                DAOFactory.BCardDAO.LoadByNpcMonsterVNum(npcMonster.NpcMonsterVNum).ToList().ForEach(s => npcMonsterObj.BCards.Add(new BCard((s))));
                _npcs.Add(npcMonsterObj);
            });
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("NPCMONSTERS_LOADED"), _npcs.Count));

            // intialize recipes
            _recipes = new ThreadSafeSortedList<short, Recipe>();
            Parallel.ForEach(DAOFactory.RecipeDAO.LoadAll(), recipeGrouping =>
            {
                Recipe recipe = new Recipe(recipeGrouping);
                _recipes[recipeGrouping.RecipeId] = recipe;
                recipe.Initialize();
            });
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("RECIPES_LOADED"), _recipes.Count));

            // initialize recipelist
            _recipeLists = new ThreadSafeSortedList<int, RecipeListDTO>();
            Parallel.ForEach(DAOFactory.RecipeListDAO.LoadAll(), recipeListGrouping => _recipeLists[recipeListGrouping.RecipeListId] = recipeListGrouping);
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("RECIPELISTS_LOADED"), _recipeLists.Count));

            // initialize shopitems
            _shopItems = new ThreadSafeSortedList<int, List<ShopItemDTO>>();
            Parallel.ForEach(DAOFactory.ShopItemDAO.LoadAll().GroupBy(s => s.ShopId), shopItemGrouping => _shopItems[shopItemGrouping.Key] = shopItemGrouping.ToList());
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("SHOPITEMS_LOADED"), _shopItems.Sum(i => i.Count)));

            // initialize shopskills
            _shopSkills = new ThreadSafeSortedList<int, List<ShopSkillDTO>>();
            Parallel.ForEach(DAOFactory.ShopSkillDAO.LoadAll().GroupBy(s => s.ShopId), shopSkillGrouping => _shopSkills[shopSkillGrouping.Key] = shopSkillGrouping.ToList());
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("SHOPSKILLS_LOADED"), _shopSkills.Sum(i => i.Count)));

            // initialize shops
            _shops = new ThreadSafeSortedList<int, Shop>();
            Parallel.ForEach(DAOFactory.ShopDAO.LoadAll(), shopGrouping =>
            {
                Shop shop = new Shop(shopGrouping);
                _shops[shopGrouping.MapNpcId] = shop;
                shop.Initialize();
            });
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("SHOPS_LOADED"), _shops.Count));

            // initialize teleporters
            _teleporters = new ThreadSafeSortedList<int, List<TeleporterDTO>>();
            Parallel.ForEach(DAOFactory.TeleporterDAO.LoadAll().GroupBy(t => t.MapNpcId), teleporterGrouping => _teleporters[teleporterGrouping.Key] = teleporterGrouping.Select(t => t).ToList());
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("TELEPORTERS_LOADED"), _teleporters.Sum(i => i.Count)));

            // initialize skills
            Parallel.ForEach(DAOFactory.SkillDAO.LoadAll(), skill =>
            {
                Skill skillObj = new Skill(skill);
                skillObj.Combos.AddRange(DAOFactory.ComboDAO.LoadBySkillVnum(skillObj.SkillVNum).ToList());
                skillObj.BCards = new List<BCard>();
                DAOFactory.BCardDAO.LoadBySkillVNum(skillObj.SkillVNum).ToList().ForEach(o => skillObj.BCards.Add(new BCard(o)));
                _skills.Add(skillObj);
            });
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("SKILLS_LOADED"), _skills.Count));

            // initialize cards
            Parallel.ForEach(DAOFactory.CardDAO.LoadAll(), card =>
            {
                Card cardObj = new Card(card)
                {
                    BCards = new List<BCard>()
                };
                DAOFactory.BCardDAO.LoadByCardId(cardObj.CardId).ToList().ForEach(o => cardObj.BCards.Add(new BCard(o)));
                _cards.Add(cardObj);
            });
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("CARDS_LOADED"), _cards.Count));

            // intialize mapnpcs
            _mapNpcs = new ThreadSafeSortedList<short, List<MapNpc>>();
            Parallel.ForEach(DAOFactory.MapNpcDAO.LoadAll().GroupBy(t => t.MapId), mapNpcGrouping => _mapNpcs[mapNpcGrouping.Key] = mapNpcGrouping.Select(t => t as MapNpc).ToList());
            Logger.Info(string.Format(Language.Instance.GetMessageFromKey("MAPNPCS_LOADED"), _mapNpcs.Sum(i => i.Count)));

            try
            {
                int i = 0;
                int monstercount = 0;
                OrderablePartitioner<MapDTO> mapPartitioner = Partitioner.Create(DAOFactory.MapDAO.LoadAll(), EnumerablePartitionerOptions.NoBuffering);
                Parallel.ForEach(mapPartitioner, new ParallelOptions { MaxDegreeOfParallelism = 8 }, map =>
                {
                    Guid guid = Guid.NewGuid();
                    Map mapinfo = new Map(map.MapId, map.Data)
                    {
                        Music = map.Music,
                        Name = map.Name,
                        ShopAllowed = map.ShopAllowed
                    };
                    _maps.Add(mapinfo);
                    MapInstance newMap = new MapInstance(mapinfo, guid, map.ShopAllowed, MapInstanceType.BaseMapInstance, new InstanceBag());
                    _mapinstances.TryAdd(guid, newMap);

                    Task.Run(() => newMap.LoadPortals());
                    newMap.LoadNpcs();
                    newMap.LoadMonsters();

                    Parallel.ForEach(newMap.Npcs, mapNpc =>
                    {
                        
                        mapNpc.MapInstance = newMap;
                        newMap.AddNPC(mapNpc);
                    });
                    Parallel.ForEach(newMap.Monsters, mapMonster =>
                    {
                        mapMonster.MapInstance = newMap;
                        newMap.AddMonster(mapMonster);
                    });
                    monstercount += newMap.Monsters.Count;
                    i++;
                });
                if (i != 0)
                {
                    Logger.Info(string.Format(Language.Instance.GetMessageFromKey("MAPS_LOADED"), i));
                }
                else
                {
                    Logger.Error(Language.Instance.GetMessageFromKey("NO_MAP"));
                }
                Logger.Info(string.Format(Language.Instance.GetMessageFromKey("MAPMONSTERS_LOADED"), monstercount));
                StartedEvents = new List<EventType>();

                // initialize families
                loadFamilies();
                launchEvents();
                RefreshRanking();
                CharacterRelations = DAOFactory.CharacterRelationDAO.LoadAll().ToList();
                PenaltyLogs = DAOFactory.PenaltyLogDAO.LoadAll().ToList();
                if (DAOFactory.MapDAO.LoadById(2006) != null)
                {
                    ArenaInstance = GenerateMapInstance(2006, MapInstanceType.NormalInstance, new InstanceBag());
                    ArenaInstance.IsPVP = true;
                }
                if (DAOFactory.MapDAO.LoadById(2106) != null)
                {
                    FamilyArenaInstance = GenerateMapInstance(2106, MapInstanceType.NormalInstance, new InstanceBag());
                    FamilyArenaInstance.IsPVP = true;
                }
                loadScriptedInstances();

                XmlSerializer serializer = new XmlSerializer(typeof(QuestModel));
                QuestList = new ThreadSafeSortedList<long, QuestModel>();
                Parallel.ForEach(DAOFactory.QuestDAO.LoadAll(), s =>
                {
                    if (s.QuestData != null)
                    {
                        using (TextReader reader = new StringReader(s.QuestData))
                        {
                            QuestList[s.QuestId] = (QuestModel)serializer.Deserialize(reader);
                        }
                    }
                });

                Logger.Info(string.Format(Language.Instance.GetMessageFromKey("QUESTS_LOADED"), QuestList.Count));
            }
            catch (Exception ex)
            {
                Logger.Error("General Error", ex);
            }
            WorldId = Guid.NewGuid();
        }

        public bool IsCharacterMemberOfGroup(long characterId) => Groups?.Any(g => g.IsMemberOfGroup(characterId)) == true;

        public bool IsCharactersGroupFull(long characterId) => Groups?.Any(g => g.IsMemberOfGroup(characterId) && g.CharacterCount == (byte)g.GroupType) == true;

        public bool ItemHasRecipe(short itemVNum) => _recipeLists.Any(r => r.ItemVNum == itemVNum);

        public void JoinMiniland(ClientSession Session, ClientSession MinilandOwner)
        {
            ChangeMapInstance(Session.Character.CharacterId, MinilandOwner.Character.Miniland.MapInstanceId, 5, 8);
            if (Session.Character.Miniland.MapInstanceId != MinilandOwner.Character.Miniland.MapInstanceId)
            {
                Session.SendPacket(UserInterfaceHelper.Instance.GenerateMsg(Session.Character.MinilandMessage.Replace(' ', '^'), 0));
                Session.SendPacket(Session.Character.GenerateMlinfobr());
                MinilandOwner.Character.GeneralLogs.Add(new GeneralLogDTO { AccountId = Session.Account.AccountId, CharacterId = Session.Character.CharacterId, IpAddress = Session.IpAddress, LogData = "Miniland", LogType = "World", Timestamp = DateTime.Now });
                Session.SendPacket(MinilandOwner.Character.GenerateMinilandObjectForFriends());
            }
            else
            {
                Session.SendPacket(Session.Character.GenerateMlinfo());
                Session.SendPacket(MinilandOwner.Character.GetMinilandObjectList());
            }
            MinilandOwner.Character.Mates.Where(s => !s.IsTeamMember).ToList().ForEach(s => Session.SendPacket(s.GenerateIn()));
            Session.SendPackets(MinilandOwner.Character.GetMinilandEffects());
            Session.SendPacket(Session.Character.GenerateSay(string.Format(Language.Instance.GetMessageFromKey("MINILAND_VISITOR"), Session.Character.GeneralLogs.CountLinq(s => s.LogData == "Miniland" && s.Timestamp.Day == DateTime.Now.Day), Session.Character.GeneralLogs.CountLinq(s => s.LogData == "Miniland")), 10));
        }

        // Server
        public void Kick(string characterName)
        {
            ClientSession session = Sessions.FirstOrDefault(s => s.Character?.Name.Equals(characterName) == true);
            session?.Disconnect();
        }

        // Map
        public void LeaveMap(long id)
        {
            ClientSession session = GetSessionByCharacterId(id);
            if (session == null)
            {
                return;
            }
            session.SendPacket(UserInterfaceHelper.Instance.GenerateMapOut());
            if (!session.Character.InvisibleGm)
            {
                session.Character.Mates.Where(s => s.IsTeamMember).ToList().ForEach(s => session.CurrentMapInstance?.Broadcast(session, StaticPacketHelper.Out(UserType.Npc, s.MateTransportId), ReceiverType.AllExceptMe));
                session.CurrentMapInstance?.Broadcast(session, StaticPacketHelper.Out(UserType.Player, session.Character.CharacterId), ReceiverType.AllExceptMe);
            }
        }

        public bool MapNpcHasRecipe(int mapNpcId) => _recipeLists.Any(r => r.MapNpcId == mapNpcId);

        public int RandomNumber(int min = 0, int max = 100) => _random.Next(min, max);

        public void RefreshRanking()
        {
            TopComplimented = DAOFactory.CharacterDAO.GetTopCompliment();
            TopPoints = DAOFactory.CharacterDAO.GetTopPoints();
            TopReputation = DAOFactory.CharacterDAO.GetTopReputation();
        }

        public void RelationRefresh(long RelationId)
        {
            _inRelationRefreshMode = true;
            CommunicationServiceClient.Instance.UpdateRelation(ServerGroup, RelationId);
            SpinWait.SpinUntil(() => !_inRelationRefreshMode);
        }

        public void RemoveMapInstance(Guid MapId)
        {
            KeyValuePair<Guid, MapInstance> map = _mapinstances.FirstOrDefault(s => s.Key == MapId);
            if (!map.Equals(default))
            {
                map.Value.Dispose();
                ((IDictionary)_mapinstances).Remove(map.Key);
            }
        }

        // Map
        public void ReviveFirstPosition(long characterId)
        {
            ClientSession session = GetSessionByCharacterId(characterId);
            if (session?.Character.Hp <= 0)
            {
                if (session.CurrentMapInstance.MapInstanceType == MapInstanceType.TimeSpaceInstance || session.CurrentMapInstance.MapInstanceType == MapInstanceType.RaidInstance)
                {
                    session.Character.Hp = (int)session.Character.HPLoad();
                    session.Character.Mp = (int)session.Character.MPLoad();
                    session.CurrentMapInstance?.Broadcast(session, session.Character.GenerateRevive());
                    session.SendPacket(session.Character.GenerateStat());
                }
                else
                {
                    if (ChannelId == 51)
                    {
                        if (session.Character.IsVehicled)
                        {
                            session.Character.IsVehicled = false;
                            session.Character.Morph = 0;
                        }

                        session.Character.Hp = (int)session.Character.HPLoad();
                        session.Character.Mp = (int)session.Character.MPLoad();
                        short x = (short)(39 + RandomNumber(-2, 3));
                        short y = (short)(42 + RandomNumber(-2, 3));
                        if (session.Character.Faction == FactionType.Angel)
                        {
                            ChangeMap(session.Character.CharacterId, 130, x, y);
                        }
                        else if (session.Character.Faction == FactionType.Demon)
                        {
                            ChangeMap(session.Character.CharacterId, 131, x, y);
                        }

                        return;
                    }
                    session.Character.Hp = 1;
                    session.Character.Mp = 1;
                    if (session.CurrentMapInstance.MapInstanceType == MapInstanceType.BaseMapInstance)
                    {
                        RespawnMapTypeDTO resp = session.Character.Respawn;
                        short x = (short)(resp.DefaultX + RandomNumber(-3, 3));
                        short y = (short)(resp.DefaultY + RandomNumber(-3, 3));
                        ChangeMap(session.Character.CharacterId, resp.DefaultMapId, x, y);
                    }
                    else
                    {
                        Instance.ChangeMap(session.Character.CharacterId, session.Character.MapId, session.Character.MapX, session.Character.MapY);
                    }
                    session.CurrentMapInstance?.Broadcast(session, session.Character.GenerateTp());
                    session.CurrentMapInstance?.Broadcast(session.Character.GenerateRevive());
                    session.SendPacket(session.Character.GenerateStat());
                }
            }
        }

        public void SaveAll()
        {
            CommunicationServiceClient.Instance.CleanupOutdatedSession();
            foreach (ClientSession sess in Sessions)
            {
                sess.Character?.Save();
            }
            DAOFactory.BazaarItemDAO.RemoveOutDated();
        }

        public void SetProperty(long charId, string property, object value)
        {
            ClientSession session = GetSessionByCharacterId(charId);
            if (session == null)
            {
                return;
            }
            PropertyInfo propertyinfo = session.Character.GetType().GetProperty(property);
            propertyinfo.SetValue(session.Character, value, null);
        }

        public void Shout(string message)
        {
            Instance.Broadcast(UserInterfaceHelper.Instance.GenerateSay($"({Language.Instance.GetMessageFromKey("ADMINISTRATOR")}){message}", 10));
            Instance.Broadcast(UserInterfaceHelper.Instance.GenerateMsg(message, 2));
        }

        public async void ShutdownTask()
        {
            Shout(string.Format(Language.Instance.GetMessageFromKey("SHUTDOWN_MIN"), 5));
            for (int i = 0; i < 60 * 4; i++)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                if (Instance.ShutdownStop)
                {
                    Instance.ShutdownStop = false;
                    return;
                }
            }
            Shout(string.Format(Language.Instance.GetMessageFromKey("SHUTDOWN_MIN"), 1));
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                if (Instance.ShutdownStop)
                {
                    Instance.ShutdownStop = false;
                    return;
                }
            }
            Shout(string.Format(Language.Instance.GetMessageFromKey("SHUTDOWN_SEC"), 30));
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                if (Instance.ShutdownStop)
                {
                    Instance.ShutdownStop = false;
                    return;
                }
            }
            Shout(string.Format(Language.Instance.GetMessageFromKey("SHUTDOWN_SEC"), 10));
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                if (Instance.ShutdownStop)
                {
                    Instance.ShutdownStop = false;
                    return;
                }
            }
            InShutdown = true;
            Instance.SaveAll();
            CommunicationServiceClient.Instance.UnregisterWorldServer(WorldId);
            if (IsReboot)
            {
                if (ChannelId == 51)
                {
                    Thread.Sleep(16000);
                }
                else
                {
                    Thread.Sleep(ChannelId - 1 * 2000);
                }
                Process.Start("OpenNos.World.exe", "--nomsg");
            }
            Environment.Exit(0);
        }

        public void TeleportOnRandomPlaceInMap(ClientSession Session, Guid guid)
        {
            MapInstance map = GetMapInstance(guid);
            if (guid != default)
            {
                MapCell pos = map.Map.GetRandomPosition();
                ChangeMapInstance(Session.Character.CharacterId, guid, pos.X, pos.Y);
            }
        }

        // Server
        public void UpdateGroup(long charId)
        {
            try
            {
                if (Groups != null)
                {
                    Group myGroup = Groups.Find(s => s.IsMemberOfGroup(charId));
                    if (myGroup == null)
                    {
                        return;
                    }
                    ThreadSafeGenericList<ClientSession> groupMembers = Groups.Find(s => s.IsMemberOfGroup(charId))?.Characters;
                    if (groupMembers != null)
                    {
                        foreach (ClientSession session in groupMembers.GetAllItems())
                        {
                            session.SendPacket(session.Character.GeneratePinit());
                            session.SendPackets(session.Character.GeneratePst());
                            session.SendPacket(session.Character.GenerateStat());
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        internal List<NpcMonsterSkill> GetNpcMonsterSkillsByMonsterVNum(short npcMonsterVNum) => _monsterSkills.ContainsKey(npcMonsterVNum) ? _monsterSkills[npcMonsterVNum] : new List<NpcMonsterSkill>();

        internal Shop GetShopByMapNpcId(int mapNpcId) => _shops.ContainsKey(mapNpcId) ? _shops[mapNpcId] : null;

        internal List<ShopItemDTO> GetShopItemsByShopId(int shopId) => _shopItems.ContainsKey(shopId) ? _shopItems[shopId] : new List<ShopItemDTO>();

        internal List<ShopSkillDTO> GetShopSkillsByShopId(int shopId) => _shopSkills.ContainsKey(shopId) ? _shopSkills[shopId] : new List<ShopSkillDTO>();

        internal List<TeleporterDTO> GetTeleportersByNpcVNum(short npcMonsterVNum)
        {
            if (_teleporters?.ContainsKey(npcMonsterVNum) == true)
            {
                return _teleporters[npcMonsterVNum];
            }
            return new List<TeleporterDTO>();
        }

        internal void StopServer()
        {
            Instance.ShutdownStop = true;
            Instance.TaskShutdown = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _monsterDrops.Dispose();
                GroupsThreadSafe.Dispose();
                _monsterSkills.Dispose();
                _shopSkills.Dispose();
                _shopItems.Dispose();
                _shops.Dispose();
                _recipes.Dispose();
                _mapNpcs.Dispose();
                _teleporters.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        private void act4Process()
        {
            if (ChannelId != 51)
            {
                return;
            }

            MapInstance angelMapInstance = GetMapInstance(GetBaseMapInstanceIdByMapId(132));
            MapInstance demonMapInstance = GetMapInstance(GetBaseMapInstanceIdByMapId(133));

            void summonMukraju(MapInstance instance, byte faction)
            {
                MapMonster monster = new MapMonster
                {
                    MonsterVNum = 556,
                    MapY = (faction == 1 ? (short)92 : (short)95),
                    MapX = (faction == 1 ? (short)114 : (short)20),
                    MapId = (short)(131 + faction),
                    IsMoving = true,
                    MapMonsterId = instance.GetNextMonsterId(),
                    ShouldRespawn = false
                };
                monster.Initialize(instance);
                instance.AddMonster(monster);
                instance.Broadcast(monster.GenerateIn());
            }

            int createRaid(byte faction)
            {
                MapInstanceType raidType = MapInstanceType.Act4Morcos;
                int rng = RandomNumber(1, 5);
                switch (rng)
                {
                    case 2:
                        raidType = MapInstanceType.Act4Hatus;
                        break;

                    case 3:
                        raidType = MapInstanceType.Act4Calvina;
                        break;

                    case 4:
                        raidType = MapInstanceType.Act4Berios;
                        break;
                }
                Event.Act4Raid.GenerateRaid(raidType, faction);
                return rng;
            }

            if (Act4AngelStat.Percentage > 10000)
            {
                Act4AngelStat.Mode = 1;
                Act4AngelStat.Percentage = 0;
                Act4AngelStat.TotalTime = 300;
                summonMukraju(angelMapInstance, 1);
            }

            if (Act4AngelStat.Mode == 1 && !angelMapInstance.Monsters.Any(s => s.MonsterVNum == 556))
            {
                Act4AngelStat.Mode = 3;
                Act4AngelStat.TotalTime = 3600;

                switch (createRaid(1))
                {
                    case 1:
                        Act4AngelStat.IsMorcos = true;
                        break;

                    case 2:
                        Act4AngelStat.IsHatus = true;
                        break;

                    case 3:
                        Act4AngelStat.IsCalvina = true;
                        break;

                    case 4:
                        Act4AngelStat.IsBerios = true;
                        break;
                }
            }

            if (Act4DemonStat.Percentage > 10000)
            {
                Act4DemonStat.Mode = 1;
                Act4DemonStat.Percentage = 0;
                Act4DemonStat.TotalTime = 300;
                summonMukraju(demonMapInstance, 2);
            }

            if (Act4DemonStat.Mode == 1 && !demonMapInstance.Monsters.Any(s => s.MonsterVNum == 556))
            {
                Act4DemonStat.Mode = 3;
                Act4DemonStat.TotalTime = 3600;

                switch (createRaid(2))
                {
                    case 1:
                        Act4DemonStat.IsMorcos = true;
                        break;

                    case 2:
                        Act4DemonStat.IsHatus = true;
                        break;

                    case 3:
                        Act4DemonStat.IsCalvina = true;
                        break;

                    case 4:
                        Act4DemonStat.IsBerios = true;
                        break;
                }
            }

            Parallel.ForEach(Sessions, sess => sess.SendPacket(sess.Character.GenerateFc()));
        }

        // Server
        private void botProcess()
        {
            try
            {
                Shout(Language.Instance.GetMessageFromKey($"BOT_MESSAGE_{RandomNumber(0, 5)}"));
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private void groupProcess()
        {
            try
            {
                if (Groups != null)
                {
                    Parallel.ForEach(Groups, grp =>
                    {
                        foreach (ClientSession session in grp.Characters.GetAllItems())
                        {
                            session.SendPackets(grp.GeneratePst(session));
                        }
                    });
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private void launchEvents()
        {
            GroupsThreadSafe = new ThreadSafeSortedList<long, Group>();

            Observable.Interval(TimeSpan.FromMinutes(5)).Subscribe(x => saveAllProcess());

            Observable.Interval(TimeSpan.FromMinutes(1)).Subscribe(x => act4Process());

            Observable.Interval(TimeSpan.FromSeconds(2)).Subscribe(x => groupProcess());

            Observable.Interval(TimeSpan.FromHours(3)).Subscribe(x => botProcess());

            Observable.Interval(TimeSpan.FromMinutes(1)).Subscribe(x => maintenanceProcess());

            EventHelper.Instance.RunEvent(new EventContainer(Instance.GetMapInstance(Instance.GetBaseMapInstanceIdByMapId(98)), EventActionType.NPCSEFFECTCHANGESTATE, true));
            Parallel.ForEach(Schedules, schedule => Observable.Timer(TimeSpan.FromSeconds(EventHelper.Instance.GetMilisecondsBeforeTime(schedule.Time).TotalSeconds), TimeSpan.FromDays(1)).Subscribe(e => EventHelper.Instance.GenerateEvent(schedule.Event)));
            EventHelper.Instance.GenerateEvent(EventType.ACT4SHIP);

            Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(x => removeItemProcess());
            Observable.Interval(TimeSpan.FromMilliseconds(400)).Subscribe(x =>
            {
                Parallel.ForEach(_mapinstances, map =>
                {
                    Parallel.ForEach(map.Value.Npcs, npc => npc.StartLife());
                    Parallel.ForEach(map.Value.Monsters, monster => monster.StartLife());
                });
            });

            CommunicationServiceClient.Instance.SessionKickedEvent += onSessionKicked;
            CommunicationServiceClient.Instance.MessageSentToCharacter += onMessageSentToCharacter;
            CommunicationServiceClient.Instance.FamilyRefresh += onFamilyRefresh;
            CommunicationServiceClient.Instance.RelationRefresh += onRelationRefresh;
            CommunicationServiceClient.Instance.BazaarRefresh += onBazaarRefresh;
            CommunicationServiceClient.Instance.PenaltyLogRefresh += onPenaltyLogRefresh;
            CommunicationServiceClient.Instance.GlobalEvent += onGlobalEvent;
            CommunicationServiceClient.Instance.ShutdownEvent += onShutdown;
            CommunicationServiceClient.Instance.RestartEvent += onShutdown;
            ConfigurationServiceClient.Instance.ConfigurationUpdate += onConfiguratinEvent; ;
            MailServiceClient.Instance.MailSent += onMailSent;
            _lastGroupId = 1;
        }

        private void onMailSent(object sender, EventArgs e)
        {
            Mail mail = (Mail)sender;

            ClientSession session = GetSessionByCharacterId(mail.IsSenderCopy ? mail.SenderId : mail.ReceiverId);
            if(session != null)
            {
                MailDTO mailDTO = new MailDTO
                {
                    AttachmentAmount = mail.AttachmentAmount,
                    AttachmentRarity = mail.AttachmentRarity,
                    AttachmentUpgrade = mail.AttachmentUpgrade,
                    AttachmentVNum = mail.AttachmentVNum,
                    Date = mail.Date,
                    EqPacket = mail.EqPacket,
                    IsOpened = mail.IsOpened,
                    IsSenderCopy = mail.IsSenderCopy,
                    MailId = mail.MailId,
                    Message = mail.Message,
                    ReceiverId = mail.ReceiverId,
                    SenderClass = mail.SenderClass,
                    SenderGender = mail.SenderGender,
                    SenderHairColor = mail.SenderHairColor,
                    SenderHairStyle = mail.SenderHairStyle,
                    SenderId = mail.SenderId,
                    SenderMorphId = mail.SenderMorphId,
                    Title = mail.Title
                };

                if (mail.AttachmentVNum != null)
                {
                    session.Character.MailList.Add((session.Character.MailList.Count > 0 ? session.Character.MailList.OrderBy(s => s.Key).Last().Key : 0) + 1, mailDTO);
                    session.SendPacket(session.Character.GenerateParcel(mailDTO));
                    session.SendPacket(session.Character.GenerateSay(string.Format(Language.Instance.GetMessageFromKey("ITEM_GIFTED"), GetItem(mailDTO.AttachmentVNum.Value)?.Name, mailDTO.AttachmentAmount), 12));
                }
                else
                {
                    session.Character.MailList.Add((session.Character.MailList.Count > 0 ? session.Character.MailList.OrderBy(s => s.Key).Last().Key : 0) + 1, mailDTO);
                    session.SendPacket(session.Character.GeneratePost(mailDTO, mailDTO.IsSenderCopy ? (byte)2 : (byte)1));
                }
            }
        }

        private void onConfiguratinEvent(object sender, EventArgs e) => Configuration = (ConfigurationObject)sender;

        private void loadFamilies()
        {
            FamilyList = new ThreadSafeSortedList<long, Family>();
            Parallel.ForEach(DAOFactory.FamilyDAO.LoadAll(), familyDTO =>
            {
                Family family = Family.FromDTO(familyDTO);

                family.FamilyCharacters = new List<FamilyCharacter>();
                foreach (FamilyCharacterDTO famchar in DAOFactory.FamilyCharacterDAO.LoadByFamilyId(family.FamilyId).ToList())
                {
                    family.FamilyCharacters.Add(FamilyCharacter.FromDTO(famchar));
                }
                FamilyCharacter familyCharacter = family.FamilyCharacters.Find(s => s.Authority == FamilyAuthority.Head);
                if (familyCharacter != null)
                {
                    family.Warehouse = new Inventory(Character.FromDTO(familyCharacter.Character));
                    foreach (ItemInstanceDTO inventory in DAOFactory.IteminstanceDAO.LoadByCharacterId(familyCharacter.CharacterId).Where(s => s.Type == InventoryType.FamilyWareHouse).ToList())
                    {
                        inventory.CharacterId = familyCharacter.CharacterId;
                        family.Warehouse[inventory.Id] = new ItemInstance(inventory);
                    }
                }
                family.FamilyLogs = DAOFactory.FamilyLogDAO.LoadByFamilyId(family.FamilyId).ToList();
                FamilyList[family.FamilyId] = family;
            });
        }

        private void loadScriptedInstances()
        {
            Raids = new ConcurrentBag<ScriptedInstance>();
            Parallel.ForEach(_mapinstances, map =>
            {
                foreach (ScriptedInstanceDTO si in DAOFactory.ScriptedInstanceDAO.LoadByMap(map.Value.Map.MapId).ToList())
                {
                    ScriptedInstance siObj = new ScriptedInstance(si);
                    if (siObj != null)
                    {
                        if (siObj.Type == ScriptedInstanceType.TimeSpace)
                        {
                            siObj.LoadGlobals();
                            map.Value.ScriptedInstances.Add(siObj);
                        }
                        else if (siObj.Type == ScriptedInstanceType.Raid)
                        {
                            siObj.LoadGlobals();
                            Raids.Add(siObj);
                            Portal port = new Portal()
                            {
                                Type = (byte)PortalType.Raid,
                                SourceMapId = siObj.MapId,
                                SourceX = siObj.PositionX,
                                SourceY = siObj.PositionY
                            };
                            map.Value.Portals.Add(port);
                        }
                    }
                }
            });
        }

        private void maintenanceProcess()
        {
            List<ClientSession> sessions = Sessions.Where(c => c.IsConnected).ToList();
            MaintenanceLogDTO maintenanceLog = DAOFactory.MaintenanceLogDAO.LoadFirst();
            if (maintenanceLog != null)
            {
                if (maintenanceLog.DateStart <= DateTime.Now)
                {
                    Logger.LogUserEvent("MAINTENANCE_STATE", "Caller: ServerManager", $"[Maintenance]{Language.Instance.GetMessageFromKey("MAINTENANCE_PLANNED")}");
                    sessions.Where(s => s.Account.Authority < AuthorityType.Moderator).ToList().ForEach(session => session.Disconnect());
                }
                else if (maintenanceLog.DateStart <= DateTime.Now.AddMinutes(5))
                {
                    int min = (maintenanceLog.DateStart - DateTime.Now).Minutes;
                    if (min != 0)
                    {
                        Shout($"Maintenance will begin in {min} minutes");
                    }
                }
            }
        }

        private void onBazaarRefresh(object sender, EventArgs e)
        {
            long BazaarId = (long)sender;
            BazaarItemDTO bzdto = DAOFactory.BazaarItemDAO.LoadById(BazaarId);
            BazaarItemLink bzlink = BazaarList.Find(s => s.BazaarItem.BazaarItemId == BazaarId);
            lock (BazaarList)
            {
                if (bzdto != null)
                {
                    CharacterDTO chara = DAOFactory.CharacterDAO.LoadById(bzdto.SellerId);
                    if (bzlink != null)
                    {
                        BazaarList.Remove(bzlink);
                        bzlink.BazaarItem = bzdto;
                        bzlink.Owner = chara.Name;
                        bzlink.Item = new ItemInstance(DAOFactory.IteminstanceDAO.LoadById(bzdto.ItemInstanceId));
                        BazaarList.Add(bzlink);
                    }
                    else
                    {
                        BazaarItemLink item = new BazaarItemLink
                        {
                            BazaarItem = bzdto
                        };
                        if (chara != null)
                        {
                            item.Owner = chara.Name;
                            item.Item = new ItemInstance(DAOFactory.IteminstanceDAO.LoadById(bzdto.ItemInstanceId));
                        }
                        BazaarList.Add(item);
                    }
                }
                else if (bzlink != null)
                {
                    BazaarList.Remove(bzlink);
                }
            }
            InBazaarRefreshMode = false;
        }

        private void onFamilyRefresh(object sender, EventArgs e)
        {
            long FamilyId = (long)sender;
            FamilyDTO famdto = DAOFactory.FamilyDAO.LoadById(FamilyId);
            Family fam = FamilyList[FamilyId];
            lock (FamilyList)
            {
                if (famdto != null)
                {
                    Family newFam = (Family)famdto;
                    if (fam != null)
                    {
                        newFam.LandOfDeath = fam.LandOfDeath;
                        newFam.Act4Raid = fam.Act4Raid;
                        newFam.Act4RaidBossMap = fam.Act4RaidBossMap;
                    }

                    newFam.FamilyCharacters = new List<FamilyCharacter>();
                    foreach (FamilyCharacterDTO famchar in DAOFactory.FamilyCharacterDAO.LoadByFamilyId(famdto.FamilyId).ToList())
                    {
                        newFam.FamilyCharacters.Add((FamilyCharacter)famchar);
                    }
                    FamilyCharacter familyCharacter = newFam.FamilyCharacters.Find(s => s.Authority == FamilyAuthority.Head);
                    if (familyCharacter != null)
                    {
                        newFam.Warehouse = new Inventory((Character)familyCharacter.Character);
                        foreach (ItemInstanceDTO inventory in DAOFactory.IteminstanceDAO.LoadByCharacterId(familyCharacter.CharacterId).Where(s => s.Type == InventoryType.FamilyWareHouse).ToList())
                        {
                            inventory.CharacterId = familyCharacter.CharacterId;
                            newFam.Warehouse[inventory.Id] = new ItemInstance(inventory);
                        }
                    }
                    newFam.FamilyLogs = DAOFactory.FamilyLogDAO.LoadByFamilyId(famdto.FamilyId).ToList();
                    FamilyList[FamilyId] = newFam;

                    foreach (ClientSession sess in Sessions.Where(s => newFam.FamilyCharacters.Any(f => f.CharacterId.Equals(s.Character.CharacterId))))
                    {
                        sess.Character.Family = newFam;
                    }
                }
                else if (fam != null)
                {
                    lock (FamilyList)
                    {
                        FamilyList.Remove(fam.FamilyId);
                    }
                    foreach (ClientSession sess in Sessions.Where(s => fam.FamilyCharacters.Any(f => f.CharacterId.Equals(s.Character.CharacterId))))
                    {
                        sess.Character.Family = null;
                        sess.SendPacket(sess.Character.GenerateGidx());
                    }
                }
            }
        }

        private void onGlobalEvent(object sender, EventArgs e) => EventHelper.Instance.GenerateEvent((EventType)sender);

        private void onMessageSentToCharacter(object sender, EventArgs e)
        {
            if (sender != null)
            {
                SCSCharacterMessage message = (SCSCharacterMessage)sender;

                ClientSession targetSession = Sessions.SingleOrDefault(s => s.Character.CharacterId == message.DestinationCharacterId);
                switch (message.Type)
                {
                    case MessageType.WhisperGM:
                    case MessageType.Whisper:
                        if (targetSession == null || (message.Type == MessageType.WhisperGM && targetSession.Account.Authority != AuthorityType.GameMaster))
                        {
                            return;
                        }

                        if (targetSession.Character.GmPvtBlock)
                        {
                            CommunicationServiceClient.Instance.SendMessageToCharacter(new SCSCharacterMessage()
                            {
                                DestinationCharacterId = message.SourceCharacterId,
                                SourceCharacterId = message.DestinationCharacterId.Value,
                                SourceWorldId = WorldId,
                                Message = targetSession.Character.GenerateSay(Language.Instance.GetMessageFromKey("GM_CHAT_BLOCKED"), 10),
                                Type = MessageType.PrivateChat
                            });
                        }
                        else if (targetSession.Character.WhisperBlocked)
                        {
                            CommunicationServiceClient.Instance.SendMessageToCharacter(new SCSCharacterMessage()
                            {
                                DestinationCharacterId = message.SourceCharacterId,
                                SourceCharacterId = message.DestinationCharacterId.Value,
                                SourceWorldId = WorldId,
                                Message = UserInterfaceHelper.Instance.GenerateMsg(Language.Instance.GetMessageFromKey("USER_WHISPER_BLOCKED"), 0),
                                Type = MessageType.PrivateChat
                            });
                        }
                        else
                        {
                            if (message.SourceWorldId != WorldId)
                            {
                                CommunicationServiceClient.Instance.SendMessageToCharacter(new SCSCharacterMessage()
                                {
                                    DestinationCharacterId = message.SourceCharacterId,
                                    SourceCharacterId = message.DestinationCharacterId.Value,
                                    SourceWorldId = WorldId,
                                    Message = targetSession.Character.GenerateSay(string.Format(Language.Instance.GetMessageFromKey("MESSAGE_SENT_TO_CHARACTER"), targetSession.Character.Name, ChannelId), 11),
                                    Type = MessageType.PrivateChat
                                });
                                targetSession.SendPacket($"{message.Message} <{Language.Instance.GetMessageFromKey("CHANNEL")}: {CommunicationServiceClient.Instance.GetChannelIdByWorldId(message.SourceWorldId)}>");
                            }
                            else
                            {
                                targetSession.SendPacket(message.Message);
                            }
                        }
                        break;

                    case MessageType.Shout:
                        Shout(message.Message);
                        break;

                    case MessageType.PrivateChat:
                        targetSession?.SendPacket(message.Message);
                        break;

                    case MessageType.FamilyChat:
                        if (message.DestinationCharacterId.HasValue && message.SourceWorldId != WorldId)
                        {
                            Parallel.ForEach(Instance.Sessions, session =>
                            {
                                if (session.HasSelectedCharacter && session.Character.Family != null && session.Character.Family.FamilyId == message.DestinationCharacterId)
                                {
                                    session.SendPacket($"say 1 0 6 <{Language.Instance.GetMessageFromKey("CHANNEL")}: {CommunicationServiceClient.Instance.GetChannelIdByWorldId(message.SourceWorldId)}>{message.Message}");
                                }
                            });
                        }
                        break;

                    case MessageType.Family:
                        if (message.DestinationCharacterId.HasValue)
                        {
                            Parallel.ForEach(Instance.Sessions, session =>
                            {
                                if (session.HasSelectedCharacter && session.Character.Family != null && session.Character.Family.FamilyId == message.DestinationCharacterId)
                                {
                                    session.SendPacket(message.Message);
                                }
                            });
                        }
                        break;
                }
            }
        }

        private void onPenaltyLogRefresh(object sender, EventArgs e)
        {
            int relId = (int)sender;
            PenaltyLogDTO reldto = DAOFactory.PenaltyLogDAO.LoadById(relId);
            PenaltyLogDTO rel = PenaltyLogs.Find(s => s.PenaltyLogId == relId);
            if (reldto != null)
            {
                if (rel != null)
                {
                    rel = reldto;
                }
                else
                {
                    PenaltyLogs.Add(reldto);
                }
            }
            else if (rel != null)
            {
                PenaltyLogs.Remove(rel);
            }
        }

        private void onRelationRefresh(object sender, EventArgs e)
        {
            _inRelationRefreshMode = true;
            long relId = (long)sender;
            lock (CharacterRelations)
            {
                CharacterRelationDTO reldto = DAOFactory.CharacterRelationDAO.LoadById(relId);
                CharacterRelationDTO rel = CharacterRelations.Find(s => s.CharacterRelationId == relId);
                if (reldto != null)
                {
                    if (rel != null)
                    {
                        rel = reldto;
                    }
                    else
                    {
                        CharacterRelations.Add(reldto);
                    }
                }
                else if (rel != null)
                {
                    CharacterRelations.Remove(rel);
                }
            }
            _inRelationRefreshMode = false;
        }

        private void onSessionKicked(object sender, EventArgs e)
        {
            if (sender != null)
            {
                Tuple<long?, long?> kickedSession = (Tuple<long?, long?>)sender;
                if (!kickedSession.Item1.HasValue && !kickedSession.Item2.HasValue)
                {
                    return;
                }
                ClientSession targetSession = Sessions.FirstOrDefault(s => (!kickedSession.Item1.HasValue || s.SessionId == kickedSession.Item1.Value)
                && (!kickedSession.Item1.HasValue || s.Account.AccountId == kickedSession.Item2));

                targetSession?.Disconnect();
            }
        }

        private void onShutdown(object sender, EventArgs e)
        {
            if (Instance.TaskShutdown != null)
            {
                Instance.ShutdownStop = true;
                Instance.TaskShutdown = null;
            }
            else
            {
                Instance.TaskShutdown = new Task(Instance.ShutdownTask);
                Instance.TaskShutdown.Start();
            }
        }

        private void onRestart(object sender, EventArgs e)
        {
            if (Instance.TaskShutdown != null)
            {
                Instance.IsReboot = false;
                Instance.ShutdownStop = true;
                Instance.TaskShutdown = null;
            }
            else
            {
                Instance.IsReboot = true;
                Instance.TaskShutdown = new Task(Instance.ShutdownTask);
                Instance.TaskShutdown.Start();
            }
        }

        private void removeItemProcess()
        {
            try
            {
                Parallel.ForEach(Sessions.Where(c => c.IsConnected), session => session.Character?.RefreshValidity());
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private void reviveTask(ClientSession Session)
        {
            Task.Factory.StartNew(async () =>
            {
                bool revive = true;
                for (int i = 1; i <= 30; i++)
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    if (Session.Character.Hp > 0)
                    {
                        revive = false;
                        break;
                    }
                }
                if (revive)
                {
                    Instance.ReviveFirstPosition(Session.Character.CharacterId);
                }
            });
        }

        // Server
        private void saveAllProcess()
        {
            try
            {
                Logger.Info(Language.Instance.GetMessageFromKey("SAVING_ALL"));
                SaveAll();
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        #endregion
    }
}
 
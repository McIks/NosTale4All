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
using OpenNos.Domain;
using OpenNos.GameObject.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;

namespace OpenNos.GameObject.Event.GAMES
{
    public static class MeteoriteGame
    {
        #region Methods

        public static void GenerateMeteoriteGame()
        {
            ServerManager.Instance.Broadcast(UserInterfaceHelper.Instance.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("METEORITE_MINUTES"), 5), 0));
            ServerManager.Instance.Broadcast(UserInterfaceHelper.Instance.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("METEORITE_MINUTES"), 5), 1));
            Thread.Sleep(4 * 60 * 1000);
            ServerManager.Instance.Broadcast(UserInterfaceHelper.Instance.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("METEORITE_MINUTES"), 1), 0));
            ServerManager.Instance.Broadcast(UserInterfaceHelper.Instance.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("METEORITE_MINUTES"), 1), 1));
            Thread.Sleep(30 * 1000);
            ServerManager.Instance.Broadcast(UserInterfaceHelper.Instance.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("METEORITE_SECONDS"), 30), 0));
            ServerManager.Instance.Broadcast(UserInterfaceHelper.Instance.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("METEORITE_SECONDS"), 30), 1));
            Thread.Sleep(20 * 1000);
            ServerManager.Instance.Broadcast(UserInterfaceHelper.Instance.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("METEORITE_SECONDS"), 10), 0));
            ServerManager.Instance.Broadcast(UserInterfaceHelper.Instance.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("METEORITE_SECONDS"), 10), 1));
            Thread.Sleep(10 * 1000);
            ServerManager.Instance.Broadcast(UserInterfaceHelper.Instance.GenerateMsg(Language.Instance.GetMessageFromKey("METEORITE_STARTED"), 1));
            ServerManager.Instance.Broadcast("qnaml 100 #guri^506 The Meteorite Game is starting! Join now!");
            ServerManager.Instance.EventInWaiting = true;
            Thread.Sleep(30 * 1000);
            ServerManager.Instance.Sessions.Where(s => s.Character?.IsWaitingForEvent == false).ToList().ForEach(s => s.SendPacket("esf"));
            ServerManager.Instance.EventInWaiting = false;
            IEnumerable<ClientSession> sessions = ServerManager.Instance.Sessions.Where(s => s.Character?.IsWaitingForEvent == true && s.Character.MapInstance.MapInstanceType == MapInstanceType.BaseMapInstance);

            MapInstance map = ServerManager.Instance.GenerateMapInstance(2004, MapInstanceType.EventGameInstance, new InstanceBag());
            if (map != null)
            {
                foreach (ClientSession sess in sessions)
                {
                    ServerManager.Instance.TeleportOnRandomPlaceInMap(sess, map.MapInstanceId);
                }

                ServerManager.Instance.Sessions.Where(s => s.Character != null).ToList().ForEach(s => s.Character.IsWaitingForEvent = false);
                ServerManager.Instance.StartedEvents.Remove(EventType.METEORITEGAME);

                MeteoriteGameThread task = new MeteoriteGameThread();
                Observable.Timer(TimeSpan.FromSeconds(10)).Subscribe(X => task.Run(map));
            }
        }

        #endregion

        #region Classes

        public class MeteoriteGameThread
        {
            #region Members

            private MapInstance _map;

            #endregion

            #region Methods

            public void Run(MapInstance map)
            {
                _map = map;

                foreach (ClientSession sess in _map.Sessions)
                {
                    ServerManager.Instance.TeleportOnRandomPlaceInMap(sess, map.MapInstanceId);
                    if (sess.Character.IsVehicled)
                    {
                        sess.Character.RemoveVehicle();
                    }
                    if (sess.Character.UseSp)
                    {
                        sess.Character.LastSp = (DateTime.Now - Process.GetCurrentProcess().StartTime.AddSeconds(-50)).TotalSeconds;
                        ItemInstance specialist = sess.Character.Inventory.LoadBySlotAndType((byte)EquipmentType.Sp, InventoryType.Wear);
                        if (specialist != null)
                        {
                            removeSP(sess, specialist.ItemVNum);
                        }
                    }

                    sess.Character.Speed = 12;
                    sess.Character.IsVehicled = true;
                    sess.Character.Morph = 1156;
                    sess.Character.ArenaWinner = 0;
                    sess.Character.MorphUpgrade = 0;
                    sess.Character.MorphUpgrade2 = 0;
                    sess.SendPacket(sess.Character.GenerateCond());
                    sess.Character.LastSpeedChange = DateTime.Now;
                    sess.CurrentMapInstance?.Broadcast(sess.Character.GenerateCMode());
                }

                int i = 0;
                while (_map?.Sessions?.Any() == true)
                {
                    runRound(i++);
                }

                //ended
            }

            private IEnumerable<Tuple<short, int, short, short>> generateDrop(Map map, short vnum, int amountofdrop, int amount)
            {
                List<Tuple<short, int, short, short>> dropParameters = new List<Tuple<short, int, short, short>>();
                for (int i = 0; i < amountofdrop; i++)
                {
                    MapCell cell = map.GetRandomPosition();
                    dropParameters.Add(new Tuple<short, int, short, short>(vnum, amount, cell.X, cell.Y));
                }
                return dropParameters;
            }

            private void removeSP(ClientSession Session, short vnum)
            {
                if (Session?.HasSession == true)
                {
                    if (Session.Character.IsVehicled)
                    {
                        return;
                    }
                    List<BuffType> bufftodisable = new List<BuffType>
                    {
                        BuffType.Bad,
                        BuffType.Good,
                        BuffType.Neutral
                    };
                    Session.Character.DisableBuffs(bufftodisable);
                    Session.Character.EquipmentBCards.RemoveAll(s => s.ItemVNum.Equals(vnum));
                    Session.Character.UseSp = false;
                    Session.Character.LoadSpeed();
                    Session.SendPacket(Session.Character.GenerateCond());
                    Session.SendPacket(Session.Character.GenerateLev());
                    Session.Character.SpCooldown = 30;
                    if (Session.Character?.SkillsSp != null)
                    {
                        foreach (CharacterSkill ski in Session.Character.SkillsSp.Where(s => !s.CanBeUsed()))
                        {
                            short time = ski.Skill.Cooldown;
                            double temp = (ski.LastUse - DateTime.Now).TotalMilliseconds + (time * 100);
                            temp /= 1000;
                            Session.Character.SpCooldown = temp > Session.Character.SpCooldown ? (int)temp : Session.Character.SpCooldown;
                        }
                    }
                    Session.SendPacket(Session.Character.GenerateSay(string.Format(Language.Instance.GetMessageFromKey("STAY_TIME"), Session.Character.SpCooldown), 11));
                    Session.SendPacket($"sd {Session.Character.SpCooldown}");
                    Session.CurrentMapInstance?.Broadcast(Session.Character.GenerateCMode());
                    Session.CurrentMapInstance?.Broadcast(UserInterfaceHelper.Instance.GenerateGuri(6, 1, Session.Character.CharacterId), Session.Character.PositionX, Session.Character.PositionY);

                    // ms_c
                    Session.SendPacket(Session.Character.GenerateSki());
                    Session.SendPackets(Session.Character.GenerateQuicklist());
                    Session.SendPacket(Session.Character.GenerateStat());
                    Session.SendPacket(Session.Character.GenerateStatChar());

                    Logger.LogUserEvent("CHARACTER_SPECIALIST_RETURN", Session.GenerateIdentity(), $"SpCooldown: {Session.Character.SpCooldown}");

                    Observable.Timer(TimeSpan.FromMilliseconds(Session.Character.SpCooldown * 1000)).Subscribe(o =>
                    {
                        Session.SendPacket(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("TRANSFORM_DISAPPEAR"), 11));
                        Session.SendPacket("sd 0");
                    });
                }
            }

            private void runRound(int number)
            {
                int amount = 120 + (60 * number);

                int i = amount;
                while (i != 0)
                {
                    spawnCircle(number);
                    Thread.Sleep(60000 / amount);
                    i--;
                }
                Thread.Sleep(5000);
                _map.Broadcast(UserInterfaceHelper.Instance.GenerateMsg(string.Format(Language.Instance.GetMessageFromKey("METEORITE_ROUND"), number + 1), 0));
                Thread.Sleep(5000);

                // Your dropped reward
                _map.DropItems(generateDrop(_map.Map, 1046, 20, 200 * ((number + 1) > 10 ? 10 : (number + 1))).ToList());
                _map.DropItems(generateDrop(_map.Map, 1030, 10, 3 * ((number + 1) > 10 ? 10 : (number + 1))).ToList());
                _map.DropItems(generateDrop(_map.Map, 2282, 10, 3 * ((number + 1) > 10 ? 10 : (number + 1))).ToList());
                _map.DropItems(generateDrop(_map.Map, 2514, 5, 1 * ((number + 1) > 10 ? 10 : (number + 1))).ToList());
                _map.DropItems(generateDrop(_map.Map, 2515, 5, 1 * ((number + 1) > 10 ? 10 : (number + 1))).ToList());
                _map.DropItems(generateDrop(_map.Map, 2516, 5, 1 * ((number + 1) > 10 ? 10 : (number + 1))).ToList());
                _map.DropItems(generateDrop(_map.Map, 2517, 5, 1 * ((number + 1) > 10 ? 10 : (number + 1))).ToList());
                _map.DropItems(generateDrop(_map.Map, 2518, 5, 1 * ((number + 1) > 10 ? 10 : (number + 1))).ToList());
                _map.DropItems(generateDrop(_map.Map, 2519, 5, 1 * ((number + 1) > 10 ? 10 : (number + 1))).ToList());
                _map.DropItems(generateDrop(_map.Map, 2520, 5, 1 * ((number + 1) > 10 ? 10 : (number + 1))).ToList());
                _map.DropItems(generateDrop(_map.Map, 2521, 5, 1 * ((number + 1) > 10 ? 10 : (number + 1))).ToList());
                foreach (ClientSession session in _map.Sessions)
                {
                    // Your reward that every player should get
                }

                Thread.Sleep(30000);
            }

            private void spawnCircle(int round)
            {
                if (_map != null)
                {
                    MapCell cell = _map.Map.GetRandomPosition();

                    int circleId = _map.GetNextMonsterId();

                    MapMonster circle = new MapMonster() { MonsterVNum = 2018, MapX = cell.X, MapY = cell.Y, MapMonsterId = circleId, IsHostile = false, IsMoving = false, ShouldRespawn = false };
                    circle.Initialize(_map);
                    circle.NoAggresiveIcon = true;
                    _map.AddMonster(circle);
                    _map.Broadcast(circle.GenerateIn());
                    _map.Broadcast(StaticPacketHelper.GenerateEff(UserType.Monster, circleId, 4660));
                    Observable.Timer(TimeSpan.FromSeconds(3)).Subscribe(observer =>
                    {
                        if (_map != null)
                        {
                            _map.Broadcast(StaticPacketHelper.SkillUsed(UserType.Monster, circleId, 3, circleId, 1220, 220, 0, 4983, cell.X, cell.Y, true, 0, 65535, 0, 0));
                            foreach (Character character in _map.GetCharactersInRange(cell.X, cell.Y, 2))
                            {
                                if (!_map.Sessions.Skip(3).Any())
                                {
                                    // Your reward for the last three living players
                                }
                                character.RemoveVehicle();
                                character.GetDamage(655350);
                                Observable.Timer(TimeSpan.FromMilliseconds(1000)).Subscribe(o => ServerManager.Instance.AskRevive(character.CharacterId));
                            }
                            _map.RemoveMonster(circle);
                            _map.Broadcast(StaticPacketHelper.Out(UserType.Monster, circle.MapMonsterId));
                        }
                    });
                }
            }

            #endregion
        }

        #endregion
    }
}
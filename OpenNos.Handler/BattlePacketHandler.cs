﻿/*
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
using OpenNos.Data;
using OpenNos.Domain;
using OpenNos.GameObject;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenNos.Handler
{
    public class BattlePacketHandler : IPacketHandler
    {
        #region Members

        private readonly ClientSession _session;

        #endregion

        #region Instantiation

        public BattlePacketHandler(ClientSession session)
        {
            _session = session;
        }

        #endregion

        #region Properties

        public ClientSession Session
        {
            get
            {
                return _session;
            }
        }

        #endregion

        #region Methods

        [Packet("mtlist")]
        public void SpecialZoneHit(string packet)
        {
            PenaltyLogDTO penalty = Session.Account.PenaltyLogs.OrderByDescending(s => s.DateEnd).FirstOrDefault();
            if (Session.Character.IsMuted())
            {
                if (Session.Character.Gender == 1)
                {
                    Session.SendPacket("cancel 0 0");
                    ServerManager.Instance.Broadcast(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("MUTED_FEMALE"), 1));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 11));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 12));
                    return;
                }
                else
                {
                    Session.SendPacket("cancel 0 0");
                    ServerManager.Instance.Broadcast(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("MUTED_MALE"), 1));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 11));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 12));
                    return;
                }
            }
            if ((DateTime.Now - Session.Character.LastTransform).TotalSeconds < 3)
            {
                Session.SendPacket("cancel 0 0");
                Session.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("CANT_ATTACKNOW"), 0));
                return;
            }
            if (Session.Character.IsVehicled)
            {
                Session.SendPacket("cancel 0 0");
                return;
            }
            Logger.Debug(packet, Session.SessionId);
            string[] packetsplit = packet.Split(' ');
            ushort damage = 0;
            int hitmode = 0;
            if (packetsplit.Length > 3)
            {
                for (int i = 3; i < packetsplit.Length - 1; i += 2)
                {
                    List<CharacterSkill> skills = Session.Character.UseSp ? Session.Character.SkillsSp.GetAllItems() : Session.Character.Skills.GetAllItems();
                    if (skills != null)
                    {
                        short CastId = -1;
                        short MapMonsterId = -1;
                        if (short.TryParse(packetsplit[i], out CastId) && short.TryParse(packetsplit[i + 1], out MapMonsterId))
                        {
                            Task t = Task.Factory.StartNew((Func<Task>)(async () =>
                            {
                                CharacterSkill ski = skills.FirstOrDefault(s => s.Skill.CastId == CastId);
                                MapMonster mon = Session.CurrentMap.GetMonster(MapMonsterId);
                                if (mon != null && ski != null && mon.CurrentHp > 0)
                                {
                                    Session.Character.LastSkill = DateTime.Now;
                                    damage = GenerateDamage(mon.MapMonsterId, ski.Skill, ref hitmode);
                                    Session.CurrentMap?.Broadcast($"su 1 {Session.Character.CharacterId} 3 {mon.MapMonsterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {ski.Skill.Effect} {Session.Character.MapX} {Session.Character.MapY} {(mon.Alive ? 1 : 0)} {(int)(((float)mon.CurrentHp / (float)ServerManager.GetNpc(mon.MonsterVNum).MaxHP) * 100)} {damage} 0 {ski.Skill.SkillType - 1}");
                                    GenerateKillBonus(mon.MapMonsterId);
                                }

                                await Task.Delay((ski.Skill.Cooldown) * 100);
                                Session.SendPacket($"sr {CastId}");
                            }));
                        }
                    }
                }
            }
        }

        public void TargetHit(int castingId, int targetId)
        {
            IList<string> broadcastPackets = new List<string>();

            List<CharacterSkill> skills = Session.Character.UseSp ? Session.Character.SkillsSp.GetAllItems() : Session.Character.Skills.GetAllItems(); ;
            bool notcancel = false;
            if ((DateTime.Now - Session.Character.LastTransform).TotalSeconds < 3)
            {
                Session.SendPacket("cancel 0 0");
                Session.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("CANT_ATTACK"), 0));
                return;
            }
            if (skills != null)
            {
                ushort damage = 0;
                int hitmode = 0;
                CharacterSkill ski = skills.FirstOrDefault(s => s.Skill?.CastId == castingId && s.Skill?.UpgradeSkill == 0);
                Session.SendPacket("ms_c 0");
                if (!Session.Character.WeaponLoaded(ski))
                {
                    Session.SendPacket("cancel 2 0");
                    return;
                }
                for (int i = 0; i < 10 && (ski.LastUse.AddMilliseconds((ski.Skill.Cooldown) * 100) > DateTime.Now); i++)
                {
                    Thread.Sleep(100);
                    if (i == 10)
                    {
                        Session.SendPacket("cancel 2 0");
                        return;
                    }
                }

                if (ski != null && Session.Character.Mp >= ski.Skill.MpCost)
                {
                    if (ski.Skill.TargetType == 1 && ski.Skill.HitType == 1)
                    {
                        Session.Character.LastSkill = DateTime.Now;
                        if (!Session.Character.HasGodMode)
                        {
                            Session.Character.Mp -= ski.Skill.MpCost;
                        }
                        if (Session.Character.UseSp && ski.Skill.CastEffect != -1)
                        {
                            Session.SendPackets(Session.Character.GenerateQuicklist());
                        }

                        Session.SendPacket(Session.Character.GenerateStat());
                        CharacterSkill skillinfo = Session.Character.Skills.GetAllItems().OrderBy(o => o.SkillVNum).FirstOrDefault(s => s.Skill.UpgradeSkill == ski.Skill.SkillVNum && s.Skill.Effect > 0 && s.Skill.SkillType == 2);
                        Session.CurrentMap?.Broadcast($"ct 1 {Session.Character.CharacterId} 1 {Session.Character.CharacterId} {ski.Skill.CastAnimation} {(skillinfo != null ? skillinfo.Skill.CastEffect : ski.Skill.CastEffect)} {ski.Skill.SkillVNum}");

                        // Generate scp
                        ski.LastUse = DateTime.Now;
                        if (ski.Skill.CastEffect != 0)
                        {
                            Thread.Sleep(ski.Skill.CastTime * 100);
                        }
                        notcancel = true;
                        MapMonster mmon;
                        broadcastPackets.Add($"su 1 {Session.Character.CharacterId} 1 {Session.Character.CharacterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {(skillinfo != null ? skillinfo.Skill.Effect : ski.Skill.Effect)} {Session.Character.MapX} {Session.Character.MapY} 1 {((int)((double)Session.Character.Hp / Session.Character.HPLoad()) * 100)} 0 -2 {ski.Skill.SkillType - 1}");
                        if (ski.Skill.TargetRange != 0)
                        {
                            foreach (MapMonster mon in Session.CurrentMap.GetListMonsterInRange(Session.Character.MapX, Session.Character.MapY, ski.Skill.TargetRange).Where(s => s.CurrentHp > 0))
                            {
                                mmon = Session.CurrentMap.GetMonster(mon.MapMonsterId);
                                if (mmon != null)
                                {
                                    damage = GenerateDamage(mon.MapMonsterId, ski.Skill, ref hitmode);
                                    broadcastPackets.Add($"su 1 {Session.Character.CharacterId} 3 {mmon.MapMonsterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {(skillinfo != null ? skillinfo.Skill.Effect : ski.Skill.Effect)} {Session.Character.MapX} {Session.Character.MapY} {(mmon.Alive ? 1 : 0)} {(int)(((float)mmon.CurrentHp / (float)ServerManager.GetNpc(mon.MonsterVNum).MaxHP) * 100)} {damage} 5 {ski.Skill.SkillType - 1}");
                                    GenerateKillBonus(mon.MapMonsterId);
                                }
                            }
                        }
                    }
                    else if (ski.Skill.TargetType == 0)
                    {
                        // if monster target
                        MapMonster monsterToAttack = Session.CurrentMap.GetMonster(targetId);
                        if (monsterToAttack != null && monsterToAttack.Alive)
                        {
                            NpcMonster monsterToAttackInfo = ServerManager.GetNpc(monsterToAttack.MonsterVNum);
                            if (ski != null && monsterToAttackInfo != null && ski.Skill != null && (ski.LastUse.AddMilliseconds((ski.Skill.Cooldown) * 100) < DateTime.Now))
                            {
                                if (Session.Character.Mp >= ski.Skill.MpCost)
                                {
                                    short distanceX = (short)(Session.Character.MapX - monsterToAttack.MapX);
                                    short distanceY = (short)(Session.Character.MapY - monsterToAttack.MapY);

                                    if (Map.GetDistance(new MapCell() { X = Session.Character.MapX, Y = Session.Character.MapY },
                                                        new MapCell() { X = monsterToAttack.MapX, Y = monsterToAttack.MapY }) <= ski.Skill.Range + (DateTime.Now - monsterToAttack.LastMove).TotalSeconds * 2 * (monsterToAttackInfo.Speed == 0 ? 1 : monsterToAttackInfo.Speed) || ski.Skill.TargetRange != 0)
                                    {
                                        Session.Character.LastSkill = DateTime.Now;
                                        damage = GenerateDamage(monsterToAttack.MapMonsterId, ski.Skill, ref hitmode);

                                        ski.LastUse = DateTime.Now;
                                        GenerateKillBonus(monsterToAttack.MapMonsterId);
                                        notcancel = true;
                                        if (!Session.Character.HasGodMode)
                                        {
                                            Session.Character.Mp -= ski.Skill.MpCost;
                                        }
                                        if (Session.Character.UseSp && ski.Skill.CastEffect != -1)
                                        {
                                            Session.SendPackets(Session.Character.GenerateQuicklist());
                                        }
                                        Session.SendPacket(Session.Character.GenerateStat());
                                        CharacterSkill characterSkillInfo = Session.Character.Skills.GetAllItems().OrderBy(o => o.SkillVNum).FirstOrDefault(s => s.Skill.UpgradeSkill == ski.Skill.SkillVNum && s.Skill.Effect > 0 && s.Skill.SkillType == 2);
                                        Session.CurrentMap?.Broadcast($"ct 1 {Session.Character.CharacterId} 3 {monsterToAttack.MapMonsterId} {ski.Skill.CastAnimation} {(characterSkillInfo != null ? characterSkillInfo.Skill.CastEffect : ski.Skill.CastEffect)} {ski.Skill.SkillVNum}");
                                        Session.Character.Skills.GetAllItems().Where(s => s.Id != ski.Id).ToList().ForEach(i => i.Hit = 0);

                                        // Generate scp
                                        ski.LastUse = DateTime.Now;
                                        if (damage == 0 || (DateTime.Now - ski.LastUse).TotalSeconds > 3)
                                        {
                                            ski.Hit = 0;
                                        }
                                        else
                                        {
                                            ski.Hit++;
                                        }
                                        if (ski.Skill.CastEffect != 0)
                                        {
                                            Thread.Sleep(ski.Skill.CastTime * 100);
                                        }

                                        ComboDTO skillCombo = ski.Skill.Combos.FirstOrDefault(s => ski.Hit == s.Hit);
                                        if (skillCombo != null)
                                        {
                                            if (ski.Skill.Combos.OrderByDescending(s => s.Hit).ElementAt(0).Hit == ski.Hit)
                                            {
                                                ski.Hit = 0;
                                            }
                                            broadcastPackets.Add($"su 1 {Session.Character.CharacterId} 3 {monsterToAttack.MapMonsterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {skillCombo.Animation} {skillCombo.Effect} {Session.Character.MapX} {Session.Character.MapY} {(monsterToAttack.Alive ? 1 : 0)} {(int)(((float)monsterToAttack.CurrentHp / (float)monsterToAttackInfo.MaxHP) * 100)} {damage} {hitmode} {ski.Skill.SkillType - 1}");
                                        }
                                        else
                                        {
                                            broadcastPackets.Add($"su 1 {Session.Character.CharacterId} 3 {monsterToAttack.MapMonsterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {(characterSkillInfo != null ? characterSkillInfo.Skill.Effect : ski.Skill.Effect)} {Session.Character.MapX} {Session.Character.MapY} {(monsterToAttack.Alive ? 1 : 0)} {(int)(((float)monsterToAttack.CurrentHp / (float)monsterToAttackInfo.MaxHP) * 100)} {damage} {hitmode} {ski.Skill.SkillType - 1}");
                                        }
                                        if (ski.Skill.TargetRange != 0)
                                        {
                                            IEnumerable<MapMonster> monstersInAOERange = Session.CurrentMap?.GetListMonsterInRange(monsterToAttack.MapX, monsterToAttack.MapY, ski.Skill.TargetRange).ToList();
                                            foreach (MapMonster mon in monstersInAOERange.Where(s => s.CurrentHp > 0))
                                            {
                                                damage = GenerateDamage(mon.MapMonsterId, ski.Skill, ref hitmode);
                                                broadcastPackets.Add($"su 1 {Session.Character.CharacterId} 3 {mon.MapMonsterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {(characterSkillInfo != null ? characterSkillInfo.Skill.Effect : ski.Skill.Effect)} {Session.Character.MapX} {Session.Character.MapY} {(mon.Alive ? 1 : 0)} {(int)(((float)mon.CurrentHp / (float)ServerManager.GetNpc(mon.MonsterVNum).MaxHP) * 100)} {damage} 5 {ski.Skill.SkillType - 1}");
                                                GenerateKillBonus(mon.MapMonsterId);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // send su packets
                    Session.CurrentMap.Broadcast(broadcastPackets.ToArray(), 10);

                    Task t = Task.Factory.StartNew((Func<Task>)(async () =>
                    {
                        await Task.Delay((ski.Skill.Cooldown) * 100);
                        Session.SendPacket($"sr {castingId}");
                    }));
                }
                else
                {
                    notcancel = false;
                    Session.SendPacket(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("NOT_ENOUGH_MP"), 10));
                }
            }
            if (!notcancel)
            {
                Session.SendPacket($"cancel 2 {targetId}");
            }
        }

        [Packet("u_s")]
        public void UseSkill(string packet)
        {
            PenaltyLogDTO penalty = Session.Account.PenaltyLogs.OrderByDescending(s => s.DateEnd).FirstOrDefault();
            if (Session.Character.IsMuted())
            {
                if (Session.Character.Gender == 1)
                {
                    Session.SendPacket("cancel 0 0");
                    Session.CurrentMap?.Broadcast(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("MUTED_FEMALE"), 1));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 11));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 12));
                }
                else
                {
                    Session.SendPacket("cancel 0 0");
                    Session.CurrentMap?.Broadcast(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("MUTED_MALE"), 1));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 11));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 12));
                }
                return;
            }
            if (Session.Character.CanFight)
            {
                Logger.Debug(packet, Session.SessionId);
                string[] packetsplit = packet.Split(' ');
                if (packetsplit.Length > 6)
                {
                    short MapX = -1, MapY = -1;
                    if (!short.TryParse(packetsplit[5], out MapX) || !short.TryParse(packetsplit[6], out MapY))
                    {
                        return;
                    }
                    Session.Character.MapX = MapX;
                    Session.Character.MapY = MapY;
                }
                byte usrType;
                if (!byte.TryParse(packetsplit[3], out usrType))
                {
                    return;
                }
                byte usertype = usrType;
                if (Session.Character.IsSitting)
                {
                    Session.Character.Rest();
                }
                if (Session.Character.IsVehicled || Session.Character.InvisibleGm)
                {
                    Session.SendPacket("cancel 0 0");
                    return;
                }
                switch (usertype)
                {
                    case (byte)UserType.Monster:
                        if (packetsplit.Length > 4)
                        {
                            if (Session.Character.Hp > 0)
                            {
                                TargetHit(Convert.ToInt32(packetsplit[2]), Convert.ToInt32(packetsplit[4]));
                            }
                        }
                        break;

                    case (byte)UserType.Player:
                        if (packetsplit.Length > 4)
                        {
                            if (Session.Character.Hp > 0 && Convert.ToInt64(packetsplit[4]) == Session.Character.CharacterId)
                            {
                                TargetHit(Convert.ToInt32(packetsplit[2]), Convert.ToInt32(packetsplit[4]));
                            }
                            else
                            {
                                Session.SendPacket("cancel 2 0");
                            }
                        }
                        break;

                    default:
                        Session.SendPacket("cancel 2 0");
                        return;
                }
            }
        }

        [Packet("u_as")]
        public void UseZonesSkill(string packet)
        {
            PenaltyLogDTO penalty = Session.Account.PenaltyLogs.OrderByDescending(s => s.DateEnd).FirstOrDefault();
            if (Session.Character.IsMuted())
            {
                if (Session.Character.Gender == 1)
                {
                    Session.SendPacket("cancel 0 0");
                    Session.CurrentMap?.Broadcast(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("MUTED_FEMALE"), 1));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 11));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 12));
                }
                else
                {
                    Session.SendPacket("cancel 0 0");
                    Session.CurrentMap?.Broadcast(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("MUTED_MALE"), 1));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 11));
                    Session.SendPacket(Session.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("MUTE_TIME"), (penalty.DateEnd - DateTime.Now).ToString("hh\\:mm\\:ss")), 12));
                }
            }
            else
            {
                if (Session.Character.LastTransform.AddSeconds(3) > DateTime.Now)
                {
                    Session.SendPacket("cancel 0 0");
                    Session.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("CANT_ATTACK"), 0));
                    return;
                }
                if (Session.Character.IsVehicled)
                {
                    Session.SendPacket("cancel 0 0");
                    return;
                }
                Logger.Debug(packet, Session.SessionId);
                if (Session.Character.CanFight)
                {
                    string[] packetsplit = packet.Split(' ');
                    if (packetsplit.Length > 4)
                    {
                        if (Session.Character.Hp > 0)
                        {
                            int CastingId;
                            short x = -1;
                            short y = -1;
                            if (!int.TryParse(packetsplit[2], out CastingId) || !short.TryParse(packetsplit[3], out x) || !short.TryParse(packetsplit[4], out y))
                            {
                                return;
                            }
                            ZoneHit(CastingId, x, y);
                        }
                    }
                }
            }
        }

        [SuppressMessage("Microsoft.StyleCop.CSharp.NamingRules", "*", Justification = "W.I.P")]
        [SuppressMessage("Microsoft.StyleCop.CSharp.LayoutRules", "*", Justification = "W.I.P")]
        [SuppressMessage("Microsoft.StyleCop.CSharp.MaintainabilityRules", "*", Justification = "W.I.P")]
        [SuppressMessage("Microsoft.StyleCop.CSharp.OrderingRules", "*", Justification = "W.I.P")]
        [SuppressMessage("Microsoft.StyleCop.CSharp.ReadabilityRules", "*", Justification = "W.I.P")]
        [SuppressMessage("Microsoft.StyleCop.CSharp.SpacingRules", "*", Justification = "W.I.P")]
        [SuppressMessage("Microsoft.StyleCop.CSharp.DocumentationRules", "*", Justification = "W.I.P")]
        private ushort GenerateDamage(int monsterid, Skill skill, ref int hitmode)
        {
            #region Definitions

            MapMonster monsterToAttack = Session.CurrentMap.GetMonster(monsterid);
            if (monsterToAttack == null)
                return 0;

            short distanceX = (short)(Session.Character.MapX - monsterToAttack.MapX);
            short distanceY = (short)(Session.Character.MapY - monsterToAttack.MapY);
            Random random = new Random();
            int generated = random.Next(0, 100);
            //int miss_chance = 20;
            int monsterDefence = 0;

            short mainUpgrade = 0;
            int mainCritChance = 4;
            int mainCritHit = 70;
            int mainMinDmg = 0;
            int mainMaxDmg = 0;
            int mainHitRate = 0;

            short secUpgrade = 0;
            int secCritChance = 0;
            int secCritHit = 0;
            int secMinDmg = 0;
            int secMaxDmg = 0;
            int secHitRate = 0;

            //int CritChance = 4;
            //int CritHit = 70;
            //int MinDmg = 0;
            //int MaxDmg = 0;
            //int HitRate = 0;
            //sbyte Upgrade = 0;

            #endregion

            #region Sp
            
            SpecialistInstance specialistInstance = Session.Character.Inventory.LoadBySlotAndType<SpecialistInstance>((byte)EquipmentType.Sp, InventoryType.Wear);

            #endregion

            #region Get Weapon Stats

            WearableInstance weapon = Session.Character.Inventory.LoadBySlotAndType<WearableInstance>((byte)EquipmentType.MainWeapon, InventoryType.Wear);
            if (weapon != null)
            {
                mainUpgrade = weapon.Upgrade;
            }

            mainMinDmg += Session.Character.MinHit;
            mainMaxDmg += Session.Character.MaxHit;
            mainHitRate += Session.Character.HitRate;
            mainCritChance += Session.Character.HitCriticalRate;
            mainCritHit += Session.Character.HitCritical;

            WearableInstance weapon2 = Session.Character.Inventory.LoadBySlotAndType<WearableInstance>((byte)EquipmentType.SecondaryWeapon, InventoryType.Wear);
            if (weapon2 != null)
            {
                secUpgrade = weapon2.Upgrade;
            }

            secMinDmg += Session.Character.MinDistance;
            secMaxDmg += Session.Character.MaxDistance;
            secHitRate += Session.Character.DistanceRate;
            secCritChance += Session.Character.DistanceCriticalRate;
            secCritHit += Session.Character.DistanceCritical;

            #endregion

            #region Switch skill.Type

            switch (skill.Type)
            {
                case 0:
                    monsterDefence = monsterToAttack.Monster.CloseDefence;
                    if (Session.Character.Class == 2)
                    {
                        mainCritHit = secCritHit;
                        mainCritChance = secCritChance;
                        mainHitRate = secHitRate;
                        mainMaxDmg = secMaxDmg;
                        mainMinDmg = secMinDmg;
                        mainUpgrade = secUpgrade;
                    }
                    break;

                case 1:
                    monsterDefence = monsterToAttack.Monster.DistanceDefence;
                    if (Session.Character.Class == 1 || Session.Character.Class == 0)
                    {
                        mainCritHit = secCritHit;
                        mainCritChance = secCritChance;
                        mainHitRate = secHitRate;
                        mainMaxDmg = secMaxDmg;
                        mainMinDmg = secMinDmg;
                        mainUpgrade = secUpgrade;
                    }
                    break;

                case 2:
                    monsterDefence = monsterToAttack.Monster.MagicDefence;
                    break;
            }
            #endregion

            #region Basic Damage Data Calculation
            if (specialistInstance != null)
            {
                mainMinDmg += specialistInstance.DamageMinimum;
                mainMaxDmg += specialistInstance.DamageMaximum;
                mainCritHit += specialistInstance.CriticalRate;
                mainCritChance += specialistInstance.CriticalLuckRate;
                mainHitRate += specialistInstance.HitRate;
            }

#warning TODO: Implement BCard damage boosts, see Issue 

            mainUpgrade -= monsterToAttack.Monster.DefenceUpgrade;
            if(mainUpgrade < -10)
            {
                mainUpgrade = -10;
            }
            else if (mainUpgrade > 10)
            {
                mainUpgrade = 10;
            }

            #endregion

            #region Detailed Calculation
            #region Base Damage 
            int baseDamage = new Random().Next(mainMinDmg, mainMaxDmg + 1);
            baseDamage += (skill.Damage / 4);
            int elementalDamage = 0; //placeholder for BCard etc...
            elementalDamage += (skill.ElementalDamage / 4);
            switch (mainUpgrade)
            {
                case -10:
                    monsterDefence += (int)(monsterDefence * 2);
                    break;

                case -9:
                    monsterDefence += (int)(monsterDefence * 1.2);
                    break;

                case -8:
                    monsterDefence += (int)(monsterDefence * 0.9);
                    break;

                case -7:
                    monsterDefence += (int)(monsterDefence * 0.65);
                    break;

                case -6:
                    monsterDefence += (int)(monsterDefence * 0.54);
                    break;

                case -5:
                    monsterDefence += (int)(monsterDefence * 0.43);
                    break;

                case -4:
                    monsterDefence += (int)(monsterDefence * 0.32);
                    break;

                case -3:
                    monsterDefence += (int)(monsterDefence * 0.22);
                    break;

                case -2:
                    monsterDefence += (int)(monsterDefence * 0.15);
                    break;

                case -1:
                    monsterDefence += (int)(monsterDefence * 0.1);
                    break;

                case 0:
                    break;

                case 1:
                    baseDamage += (int)(baseDamage * 0.1);
                    break;

                case 2:
                    baseDamage += (int)(baseDamage * 0.15);
                    break;

                case 3:
                    baseDamage += (int)(baseDamage * 0.22);
                    break;

                case 4:
                    baseDamage += (int)(baseDamage * 0.32);
                    break;

                case 5:
                    baseDamage += (int)(baseDamage * 0.43);
                    break;

                case 6:
                    baseDamage += (int)(baseDamage * 0.54);
                    break;

                case 7:
                    baseDamage += (int)(baseDamage * 0.65);
                    break;

                case 8:
                    baseDamage += (int)(baseDamage * 0.9);
                    break;

                case 9:
                    baseDamage += (int)(baseDamage * 1.2);
                    break;

                case 10:
                    baseDamage += (int)(baseDamage * 2);
                    break;
            }
            #endregion
            #region Critical Damage
            if(random.Next(100) <= mainCritChance)
            {
                if (skill.Type == 2)
                {

                }
                else if (skill.Type == 3 && Session.Character.Class != 3)
                {
                    baseDamage = (int)(baseDamage * ((mainCritHit / 100D) + 1));
                    hitmode = 3;
                }
                else
                {
                    baseDamage = (int)(baseDamage * ((mainCritHit / 100D) + 1));
                    hitmode = 3;
                }

            }
            #endregion
            #region Elementary Damage
            #region Calculate Elemental Boost + Rate
            double elementalBoost = 0;
            short monsterResistance = 0;
            switch (Session.Character.Element)
            {
                case 0:
                    break;
                case 1:
                    monsterResistance = monsterToAttack.Monster.FireResistance;
                    switch (monsterToAttack.Monster.Element)
                    {
                        case 0:
                            elementalBoost = 1.3;
                            break;
                        case 1:
                            elementalBoost = 1;
                            break;
                        case 2:
                            elementalBoost = 2;
                            break;
                        case 3:
                            elementalBoost = 0.5;
                            break;
                        case 4:
                            elementalBoost = 1.5;
                            break;
                    }
                    break;
                case 2:
                    monsterResistance = monsterToAttack.Monster.WaterResistance;
                    switch (monsterToAttack.Monster.Element)
                    {
                        case 0:
                            elementalBoost = 1.3;
                            break;
                        case 1:
                            elementalBoost = 2;
                            break;
                        case 2:
                            elementalBoost = 1;
                            break;
                        case 3:
                            elementalBoost = 1.5;
                            break;
                        case 4:
                            elementalBoost = 0.5;
                            break;
                    }
                    break;
                case 3:
                    monsterResistance = monsterToAttack.Monster.LightResistance;
                    switch (monsterToAttack.Monster.Element)
                    {
                        case 0:
                            elementalBoost = 1.3;
                            break;
                        case 1:
                            elementalBoost = 1.5;
                            break;
                        case 2:
                            elementalBoost = 0.5;
                            break;
                        case 3:
                            elementalBoost = 1;
                            break;
                        case 4:
                            elementalBoost = 2;
                            break;
                    }
                    break;
                case 4:
                    monsterResistance = monsterToAttack.Monster.DarkResistance;
                    switch (monsterToAttack.Monster.Element)
                    {
                        case 0:
                            elementalBoost = 1.3;
                            break;
                        case 1:
                            elementalBoost = 0.5;
                            break;
                        case 2:
                            elementalBoost = 1.5;
                            break;
                        case 3:
                            elementalBoost = 2;
                            break;
                        case 4:
                            elementalBoost = 1;
                            break;
                    }
                    break;
            }
            #endregion;
            if(monsterResistance < 0)
            {
                monsterResistance = 0;
            }
            elementalDamage = (int)((elementalDamage + ((elementalDamage + baseDamage) * (((Session.Character.ElementRate + Session.Character.ElementRateSP) / 100D) + 1))) * elementalBoost);
            elementalDamage = elementalDamage / 100 * (100 - monsterResistance);

            #endregion
            #region Total Damage
            int totalDamage = baseDamage + elementalDamage - monsterDefence;
            if(totalDamage < 5)
            {
                totalDamage = random.Next(1, 6);
            }
            #endregion
            #endregion

            #region Old code as of 09/27/2016
            //float[] Bonus = new float[10] { 0.1f, 0.15f, 0.22f, 0.32f, 0.43f, 0.54f, 0.65f, 0.90f, 1.20f, 2f };
            //// TODO: Add skill uprade effect on damage
            //int AEq = Convert.ToInt32(random.Next(MainMinDmg, MainMaxDmg) * (1 + (MainUpgrade > monsterinfo.DefenceUpgrade ? Bonus[MainUpgrade - monsterinfo.DefenceUpgrade - 1] : 0)));
            //int DEq = Convert.ToInt32(monsterDefence * (1 + (MainUpgrade < monsterinfo.DefenceUpgrade ? Bonus[monsterinfo.DefenceUpgrade - MainUpgrade - 1] : 0)));
            //int ABase = Convert.ToInt32(random.Next(ServersData.MinHit(Session.Character.Class, Session.Character.Level), ServersData.MaxHit(Session.Character.Class, Session.Character.Level)));
            //int Aeff = 0;            // Attack of equip given by effects like weapons, jewelry, masks, hats, res, etc .. (eg. X mask: +13 attack // Crossbow
            //int Bsp6 = 0;            // Attack power increased (IMPORTANT) This already Added when SP Point has been set
            //int Bsp7 = 0;            // Attack power increased (IMPORTANT) This already Added when SP Point has been set
            //int Asp = Convert.ToInt32((Session.Character.UseSp ? Convert.ToInt32(random.Next(specialistInstance.DamageMinimum, specialistInstance.DamageMaximum + 1)) + Bsp6 + Bsp7 + (specialistInstance.SlDamage * 10) / 200 : 0));
            //int Br7 = 0;             // Improved Damage (Bonus Rune)
            //int Br22 = 0;            // % Of damage in pvp (Bonus Rune)
            //int APg = Convert.ToInt32(((AEq + ABase + Aeff + Asp + Br7) * (1 + Br22)));
            ////Logger.Debug(String.Format("APg = (AEq({0}) +  ABase({1}) + Aeff({2}) + Asp({3}) + Br7({4})) * (1 + Br22({5})) = {6}", AEq, ABase, Aeff, Asp, Br7, Br22, APg));

            //int DBase = 0;           // Defense Of Pg Convert.ToInt32 Base (Monster Defense);
            //int Deff = 0;            // Defense given by effects of equip as weapons, jewelry, masks, hats, res, etc .. (eg. Balestra 90: +150 Defense)
            //int Dsp = 0;             // The mob have no defense given by sp points and the sl
            //int Br21 = 0;            // It reduces the opponent's defense% in PvP
            //int Br28 = 0;            // Defense for long-rage improved
            //int Br29 = 0;            // Defense for melee improved
            //int Br30 = 0;            // Improved magic defense
            //int Br31 = 0;            // % To all defense
            //int Br32 = 0;            // % To all defense in PvP
            //int DPg = Convert.ToInt32((DEq + DBase + Deff + Dsp + Br28 + Br29 + Br30) * (1 + (Br31 + Br32) - Br21));
            ////Logger.Debug(String.Format("DPg = (DEq({0}) +  DBase({1}) + Deff({2}) + Dsp({3}) + Br28({4}) + Br29({5}) + Br30({6})) * (1 + (Br31({7}) + + Br32({8}) - Br21({9}))) = {10}", DEq, DBase, Deff, Dsp, Br28, Br29, Br30, Br31, Br32, Br21, DPg));

            //int Br6 = 0;             // % of Damage
            //int Br8 = 0;             // Damage on monster with animal type
            //int Br9 = 0;             // Damage increase to enemy (demons)
            //int Br10 = 0;            // Damage increase to plant
            //int Br11 = 0;            // Damage increase on undead
            //int Br12 = 0;            // Increase damage on small monster
            //int Br13 = 0;            // Increase damage on tall monster
            //int BonusEq = 0;         // Bonus% of the weapons, known as bug 90 (ex. Arc 90 -> With a 25% probability increases damage up to 40%. and add effect 15 when damage have the bonus (damage up)
            //int At = Convert.ToInt32(((APg + skill.Damage) * (1 + (Br6 + Br8 + Br9 + Br10 + Br11 + Br12 + Br13))) * (1 + BonusEq));
            ////Logger.Debug(String.Format("At = ((APg {0} + skill.Damage {1} + 15) * (1 + (Br6 {2} + Br8 {3} + Br9 {4} + Br10 {5} + Br11 {6} + Br12 {7} + Br13 {8}))) * (1 + BonusEq{9}) = {10}", APg, skill.Damage, Br6, Br8, Br9, Br10, Br11, Br12, Br13, BonusEq, At));

            //int DSkill = 0;          // base defense (not basic) given by the skill (eg. light protection Caster Defense + lv = * 2)
            //int EffectPetPvp = 0;    // Defence given by pet on pvp(?)
            //int DefensePotion = 0;   // Defence given by potion
            //int DArmor = 0;          // Defence given by armor
            //int DPet = 0;            // Defence given by pet
            //int DOilFlower = 0;      // Defense given by the oil flower(?)
            //int Dt = Convert.ToInt32((DPg + DSkill) * (1 + EffectPetPvp) + (1 + (DOilFlower != 0 ? DOilFlower : (DefensePotion + DArmor + DPet))));
            ////Logger.Debug(String.Format("Dt = (DPg{0} + DSkill{1}) * (1 + EffectPetPvp{2}) + (1 + (DOilFlower{3} != 0 ? DOilFlower{4} : (DefensePotion{5} + DArmor{6} + DPet{7})) = {8}", DPg, DSkill, EffectPetPvp, DOilFlower, DOilFlower, DefensePotion, DArmor, DPet, Dt));

            //int AOilFlower = 0;      // Attack given by the oil flower(?)
            //int Bskl8 = 0;           // of the Iron Warrior Skin
            //int Bskl5 = 0;           // Hawkeye ranger
            //int Damage = Convert.ToInt32((At - Dt) * (1 + AOilFlower) * (1 + Bskl8) * (1 - Bskl5));
            ////Logger.Debug(String.Format("Damage: {0}", Damage));

            //int F = Convert.ToInt32(Session.Character.ElementRate / 100);
            //int Bsp5 = 0;            // Bonus SP (IMPORTANT) This already Added when SP Point has been set
            //int SLPerfect = 0;       // Bonus SP (IMPORTANT) This already Added when Perfect SP has been done
            //int Esp = Convert.ToInt32((Session.Character.UseSp ? Convert.ToInt32(specialistInstance.SlElement + Bsp5 + SLPerfect) / 200 : 0));
            //int E = Convert.ToInt32((At + 0) * (1 + (F + Esp)));
            //int Eeff = 0;            // Element given by effects of equip as weapons, jewelry, masks, hats, res
            //int ESkill = Convert.ToInt32(skill.ElementalDamage);
            //int Br1 = 0;             // Fire properties increased
            //int Br2 = 0;             // Water properties increased
            //int Br3 = 0;             // Light properties increased
            //int Br4 = 0;             // Properties of Dark increased
            //int Br5 = 0;             // Elemental properties of increased
            //int Et = Convert.ToInt32(E + Eeff + ESkill + Br1 + Br2 + Br3 + Br4 + Br5);
            ////Logger.Debug(String.Format("Et = E{0} + Eeff{1} + ESkill{2} + Br1{3} + Br2{4} + Br3{5} + Br4{6} + Br5{7} = {8}", E, Eeff, ESkill, Br1, Br2, Br3, Br4, Br5, Et));

            //float Eele = 0;
            //float EPg = Session.Character.Element;
            //// Need to add skill element
            //float EMob = monsterinfo.Element;
            //if ((EPg == 0 && EMob >= 0 && EMob < 5) || (EPg == 1 && EMob == 3) || (EPg == 2 && EMob == 4) || (EPg == 3 && EMob == 2) || (EPg == 4 && EMob == 1)) Eele = 1f; // 0 No Element | 1 Fire | 2 Water | 3 Light | Darkness
            //else if ((EPg == 1 && EMob == 1) || (EPg == 2 && EMob == 2) || (EPg == 3 && EMob == 3) || (EPg == 4 && EMob == 4)) Eele = 1f;
            //else if ((EPg == 1 && EMob >= 0) || (EPg == 2 && EMob == 0) || (EPg == 3 && EMob == 0) || (EPg == 4 && EMob == 0)) Eele = 1.3f;
            //else if ((EPg == 1 && EMob == 4) || (EPg == 2 && EMob == 3) || (EPg == 3 && EMob == 1) || (EPg == 4 && EMob == 2)) Eele = 1.5f;
            //else if ((EPg == 1 && EMob == 2) || (EPg == 2 && EMob == 1)) Eele = 2f;
            //else if ((EPg == 3 && EMob == 4) || (EPg == 4 && EMob == 3)) Eele = 3f;
            //float RGloves = monsterinfo.GetRes(skill.Element); // Resistance given by glove (eg. Fire glove comb B s4 = 50%)
            //float RShoes = 0;            // Resistance given by shoes
            //float DReff = 0;             // Resistance give by mask (eg. mask x give all resistance +4)
            //float DRskill = 0;           // Resistance given by the skill (eg. sp4 for bowman buff)
            //float Rsp = 0;               // Resistance given by the SP
            //float Rperf = 0;             // data of improvements resistance
            //float Bsp4 = 0;              // Resistance (water,fire,light and darkness
            //float Br23 = 0;              // Increase fire resistance
            //float Br24 = 0;              // Incrase water resistance
            //float Br25 = 0;              // Increase light resistance
            //float Br26 = 0;              // Increase darkness resistance
            //float Br27 = 0;              // Increase resistance
            //float Dres = RGloves + RShoes + DReff + DRskill + Rsp + Rperf + Bsp4 + Br23 + Br24 + Br25 + Br26 + Br27;
            //int AReff = 0;           // Drop in resistance given by effects of equip as weapons, jewelry, masks, hats, res, etc .. (eg. Sword 90 = -15 res to all elements)
            //int ARskill = 0;         // Drop in resistance given by the skill (es.Calo the WK = -40 res to all elements)
            //int Br16 = 0;            // Reduce all resistance of the enemy in PvP
            //int Br17 = 0;            // Reduce water resistance of enemy in PvP
            //int Br18 = 0;            // Reduce light resistance of enemy in PvP
            //int Br19 = 0;            // Reduce darkness resistance of enemy in PvP
            //int Br20 = 0;            // Reduce all defense of enemy in PvP
            //float Ares = AReff + ARskill + Br16 + Br17 + Br18 + Br19 + Br20;
            //int Ef = Convert.ToInt32((Et * Eele) * (1 - (Dres - Ares) / 100));
            ////Logger.Debug(String.Format("Ef = (Et {0} * Eele{1}) * (1 - (Dres{2} - Ares{3})) = {4}", Et, Eele, Dres, Ares, Ef));

            //int moralDefence = Session.Character.Level + /*Session.Character.Morale */ -monsterinfo.Level; //Morale Atk pg - Morale def pg
            ////short Damage = 0;

            //if (Session.Character.Class != 3)
            //{
            //    if (generated < CritChance)
            //    {
            //        hitmode = 3;
            //        MainMinDmg = (MainMinDmg + ((MainMaxDmg - MainMinDmg) / 2));
            //        short Br14 = 0;  // (Except sticks) Increase critical damage
            //        short Bsp1 = 0;  // They give the death blow (increase critical damage)
            //        short DcrEq = 0; // Decrease of critical damage from the effects of equip given as weapons, jewelry, masks, hats, res, etc .. (eg. Sword luminaire is 90 = -60% critical damage)
            //        short Bsp2 = 0;  // Decreased deathblow (decreases the critical damage)
            //        Damage = Convert.ToInt32(Damage * (1 + (MainCritHit / 100) + Br14 + Bsp1) - (DcrEq + Bsp2));
            //    }
            //}

            //int Dmob = 0; // Base damage of monster, varies in function of the lvl of the monster
            //if (monsterinfo.Level >= 1 && monsterinfo.Level <= 44) Dmob = 0;
            //else if (monsterinfo.Level >= 45 && monsterinfo.Level <= 55) Dmob = Convert.ToInt32(monsterinfo.Level * 2);
            //else if (monsterinfo.Level >= 56 && monsterinfo.Level <= 69) Dmob = Convert.ToInt32(monsterinfo.Level * 3);
            //else Dmob = Convert.ToInt32(monsterinfo.Level * 5);
            //int Bsp3 = 0;         // Decrease magic damage
            //int AttackPotion = 0; // attack given by potion
            //int Ahair = 0;        // Attack% given by hair (eg. + 5% Santa Hat)
            //int Apet = 0;         // Attack% given by the pet (eg. + 10% Fibi)

            //float rangedDistance = 1;
            //if (Session.Character.Class == 2)
            //{
            //    rangedDistance = 0.75f;
            //    for (int i = 1; i < Map.GetDistance(new MapCell { X = Session.Character.MapX, MapId = Session.Character.MapId, Y = Session.Character.MapY }, new MapCell { MapId = monsterToAttack.MapId, X = monsterToAttack.MapX, Y = monsterToAttack.MapY }); i++)
            //        rangedDistance += 0.0232f;
            //}
            //if (Session.Character.Class != 2) rangedDistance = 1;

            //int finalDamage = Convert.ToInt32((Damage + Ef + moralDefence + Dmob) * (1 - Bsp3) * (1 + (AttackPotion + Ahair + Apet)) * rangedDistance);
            ////Logger.Debug(String.Format("FinalDamage = (Damage {0} + Ef {1}  + MoralDifference{2} + Dmob{3})  (1 - Bsp3{4})  (1 + (AttackPotion{5} + Ahair{6} + Apet{7})) * RangedDistance{8} = {9}", Damage, Ef, MoralDifference, Dmob, Bsp3, AttackPotion, Ahair, Apet, RangedDistance, FinalDamage));

            //if (Session.Character.Class != 3 && !Session.Character.HasGodMode)
            //{
            //    //if (generated > 100 - miss_chance)
            //    //{
            //    //    hitmode = 1;
            //    //    finalDamage = 0;
            //    //}
            //}
            #endregion
            if (monsterToAttack.DamageList.ContainsKey(Session.Character.CharacterId))
            {
                monsterToAttack.DamageList[Session.Character.CharacterId] += totalDamage;
            }
            else
            {
                monsterToAttack.DamageList.Add(Session.Character.CharacterId, totalDamage);
            }
            if (monsterToAttack.CurrentHp <= totalDamage)
            {
                monsterToAttack.Alive = false;
                monsterToAttack.CurrentHp = 0;
                monsterToAttack.CurrentMp = 0;
                monsterToAttack.Death = DateTime.Now;                           
            }
            else
            {
                monsterToAttack.CurrentHp -= totalDamage;
            }
            ushort damage = 0;

            while (totalDamage > ushort.MaxValue)
            {
                totalDamage -= ushort.MaxValue;
            }

            damage = Convert.ToUInt16(totalDamage);
            if (monsterToAttack.IsMoving)
                monsterToAttack.Target = Session.Character.CharacterId;
            return damage;
        }

        private void GenerateKillBonus(int monsterid)
        {
            MapMonster monsterToAttack = Session.CurrentMap.GetMonster(monsterid);
            if (monsterToAttack == null || monsterToAttack.CurrentHp > 0)
            {
                return;
            }
            Random random = new Random(DateTime.Now.Millisecond & monsterid);

            // owner set
            long? Owner = monsterToAttack.DamageList.Any() ? monsterToAttack.DamageList.First().Key : (long?)null;
            Group gr = null;
            if (Owner != null)
            {
                gr = ServerManager.Instance.Groups.FirstOrDefault(g => g.IsMemberOfGroup((long)Owner));
            }

            // end owner set
            int i = 1;
            List<DropDTO> droplist = monsterToAttack.Monster.Drops.Where(s => Session.CurrentMap.MapTypes.Any(m => m.MapTypeId == s.MapTypeId) || (s.MapTypeId == null)).ToList();
            if (monsterToAttack.Monster.MonsterType != MonsterType.Special)
            {
                int RateDrop = ServerManager.DropRate;
                int x = 0;

                foreach (DropDTO drop in droplist.OrderBy(s => random.Next()))
                {
                    if (x < 4)
                    {
                        i++;
                        double rndamount = random.Next(0, 100) * random.NextDouble();
                        if (rndamount <= ((double)drop.DropChance * RateDrop) / 5000.000)
                        {
                            x++;
                            if (Session.CurrentMap.MapTypes.Any(s => s.MapTypeId == (short)MapTypeEnum.Act4) || monsterToAttack.Monster.MonsterType == MonsterType.Elite)
                            {
                                Session.Character.GiftAdd(drop.ItemVNum, (byte)drop.Amount);
                            }
                            else
                            {
                                if (gr != null)
                                {
                                    if (gr.SharingMode == (byte)GroupSharingType.ByOrder)
                                    {
                                        Owner = gr.OrderedCharacterId(Session.Character);
                                        gr.Characters.ForEach(s => s.SendPacket(s.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("ITEM_BOUND_TO"), ServerManager.GetItem(drop.ItemVNum).Name, gr.Characters.Single(c => c.Character.CharacterId == (long)Owner).Character.Name, drop.Amount), 10)));
                                    }
                                    else
                                    {
                                        gr.Characters.ForEach(s => s.SendPacket(s.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("DROPPED_ITEM"), ServerManager.GetItem(drop.ItemVNum).Name, drop.Amount), 10)));
                                    }
                                }
                                Session.CurrentMap.DropItemByMonster(Owner, drop, monsterToAttack.MapX, monsterToAttack.MapY);
                            }
                        }
                    }
                }

                int RateGold = ServerManager.GoldRate;
                int dropIt = ((random.Next(0, Session.Character.Level) < monsterToAttack.Monster.Level) ? 1 : 0);
                int lowBaseGold = random.Next(6 * monsterToAttack.Monster.Level, 12 * monsterToAttack.Monster.Level);
                int isAct52 = (Session.CurrentMap.MapTypes.Any(s => s.MapTypeId == (short)MapTypeEnum.Act52) ? 10 : 1);
                int gold = Convert.ToInt32(dropIt * lowBaseGold * RateGold * isAct52);
                gold = gold > 1000000000 ? 1000000000 : gold;
                if (gold != 0)
                {
                    DropDTO drop2 = new DropDTO()
                    {
                        Amount = gold,
                        ItemVNum = 1046
                    };

                    if (Session.CurrentMap.MapTypes.Any(s => s.MapTypeId == (short)MapTypeEnum.Act4) || monsterToAttack.Monster.MonsterType == MonsterType.Elite)
                    {
                        Session.Character.Gold += drop2.Amount;
                        if (Session.Character.Gold > 1000000000)
                        {
                            Session.Character.Gold = 1000000000;
                            Session.SendPacket(Session.Character.GenerateMsg(Language.Instance.GetMessageFromKey("MAX_GOLD"), 0));
                        }
                        Session.SendPacket(Session.Character.GenerateSay($"{Language.Instance.GetMessageFromKey("ITEM_ACQUIRED")}: {ServerManager.GetItem(drop2.ItemVNum).Name} x {drop2.Amount}", 10));
                        Session.SendPacket(Session.Character.GenerateGold());
                    }
                    else
                    {
                        if (gr != null)
                        {
                            if (gr.SharingMode == (byte)GroupSharingType.ByOrder)
                            {
                                Owner = gr.OrderedCharacterId(Session.Character);
                                gr.Characters.ForEach(s => s.SendPacket(s.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("ITEM_BOUND_TO"), ServerManager.GetItem(drop2.ItemVNum).Name, gr.Characters.Single(c => c.Character.CharacterId == (long)Owner).Character.Name, drop2.Amount), 10)));
                            }
                            else
                            {
                                gr.Characters.ForEach(s => s.SendPacket(s.Character.GenerateSay(String.Format(Language.Instance.GetMessageFromKey("DROPPED_ITEM"), ServerManager.GetItem(drop2.ItemVNum).Name, drop2.Amount), 10)));
                            }
                        }
                        Session.CurrentMap.DropItemByMonster(Owner, drop2, monsterToAttack.MapX, monsterToAttack.MapY);
                    }
                }
                if (Session.Character.Hp > 0)
                {
                    Group grp = ServerManager.Instance.Groups.FirstOrDefault(g => g.IsMemberOfGroup(Session.Character.CharacterId));
                    if (grp != null)
                    {
                        grp.Characters.Where(g => g.Character.MapId == Session.Character.MapId).ToList().ForEach(g => g.Character.GenerateXp(monsterToAttack.Monster));
                    }
                    else
                    {
                        Session.Character.GenerateXp(monsterToAttack.Monster);
                    }
                    Session.Character.GenerateDignity(monsterToAttack.Monster);
                }
            }
        }

        private void ZoneHit(int Castingid, short x, short y)
        {
            List<CharacterSkill> skills = Session.Character.UseSp ? Session.Character.SkillsSp.GetAllItems() : Session.Character.Skills.GetAllItems();
            ushort damage = 0;
            int hitmode = 0;
            CharacterSkill ski = skills.FirstOrDefault(s => s.Skill.CastId == Castingid);
            if (!Session.Character.WeaponLoaded(ski))
            {
                Session.SendPacket("cancel 2 0");
                return;
            }
            if (ski != null)
            {
                if (Session.Character.Mp >= ski.Skill.MpCost)
                {
                    Task t = Task.Factory.StartNew((Func<Task>)(async () =>
                    {
                        Session.CurrentMap?.Broadcast($"ct_n 1 {Session.Character.CharacterId} 3 -1 {ski.Skill.CastAnimation} {ski.Skill.CastEffect} {ski.Skill.SkillVNum}");
                        ski.LastUse = DateTime.Now;
                        if (!Session.Character.HasGodMode)
                        {
                            Session.Character.Mp -= ski.Skill.MpCost;
                        }
                        Session.SendPacket(Session.Character.GenerateStat());
                        ski.LastUse = DateTime.Now;
                        await Task.Delay(ski.Skill.CastTime * 100);
                        Session.Character.LastSkill = DateTime.Now;

                        Session.CurrentMap?.Broadcast($"bs 1 {Session.Character.CharacterId} {x} {y} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {ski.Skill.Effect} 0 0 1 1 0 0 0");

                        IEnumerable<MapMonster> monstersInRange = Session.CurrentMap.GetListMonsterInRange(x, y, ski.Skill.TargetRange).ToList();
                        foreach (MapMonster mon in monstersInRange.Where(s=>s.CurrentHp > 0))
                        {
                            damage = GenerateDamage(mon.MapMonsterId, ski.Skill, ref hitmode);
                            Session.CurrentMap?.Broadcast($"su 1 {Session.Character.CharacterId} 3 {mon.MapMonsterId} {ski.Skill.SkillVNum} {ski.Skill.Cooldown} {ski.Skill.AttackAnimation} {ski.Skill.Effect} {x} {y} {(mon.Alive ? 1 : 0)} {(int)(((float)mon.CurrentHp / (float)ServerManager.GetNpc(mon.MonsterVNum).MaxHP) * 100)} {damage} 5 {ski.Skill.SkillType - 1}");
                            GenerateKillBonus(mon.MapMonsterId);
                        }

                        await Task.Delay((ski.Skill.Cooldown) * 100);
                        Session.SendPacket($"sr {Castingid}");
                    }));
                }
                else
                {
                    Session.SendPacket(Session.Character.GenerateSay(Language.Instance.GetMessageFromKey("NOT_ENOUGH_MP"), 10));
                    Session.SendPacket("cancel 2 0");
                }
            }
            else
            {
                Session.SendPacket("cancel 2 0");
            }
        }

        #endregion
    }
}
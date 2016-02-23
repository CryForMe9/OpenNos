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
using OpenNos.DAL;
using OpenNos.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenNos.Import.Console
{
    public class ImportFactory
    {
        #region Members

        private readonly string _folder;

        #endregion

        #region Instantiation

        public ImportFactory(string folder)
        {
            _folder = folder;
        }

        #endregion

        #region Methods

        public void ImportItems()
        {
            string file = $"{_folder}\\Item.dat";
            IEnumerable<ItemDTO> items = DatParser.Parse<ItemDTO>(file);

            // TODO is this Parse() fully working? where to put 'items' then?

            //int i = 0;
        }

        public void ImportMaps()
        {
            string fileMapIdDat = $"{_folder}\\MapIDData.dat";
            string fileMapIdLang = $"{_folder}\\_code_{System.Configuration.ConfigurationManager.AppSettings["language"]}_MapIDData.txt";
            string filePacket = $"{_folder}\\packet.txt";
            string folderMap = $"{_folder}\\map";

            Dictionary<int, string> dictionaryId = new Dictionary<int, string>();
            Dictionary<string, string> dictionaryIdLang = new Dictionary<string, string>();
            Dictionary<int, int> dictionaryMusic = new Dictionary<int, int>();

            string line;
            int i = 0;
            using (StreamReader mapIdStream = new StreamReader(fileMapIdDat, Encoding.GetEncoding(1252)))
            {
                while ((line = mapIdStream.ReadLine()) != null)
                {
                    string[] linesave = line.Split(' ');
                    if (linesave.Length > 1)
                    {
                        int mapid;
                        if (!int.TryParse(linesave[0], out mapid)) continue;

                        if (!dictionaryId.ContainsKey(mapid))
                            dictionaryId.Add(mapid, linesave[4]);
                    }
                }
                mapIdStream.Close();
            }

            using (StreamReader mapIdLangStream = new StreamReader(fileMapIdLang, Encoding.GetEncoding(1252)))
            {
                while ((line = mapIdLangStream.ReadLine()) != null)
                {
                    string[] linesave = line.Split('\t');
                    if (linesave.Length > 1)
                    {
                        dictionaryIdLang.Add(linesave[0], linesave[1]);
                    }
                }
                mapIdLangStream.Close();
            }

            using (StreamReader packetTxtStream = new StreamReader(filePacket, Encoding.GetEncoding(1252)))
            {
                while ((line = packetTxtStream.ReadLine()) != null)
                {
                    string[] linesave = line.Split(' ');
                    if (linesave.Length > 7 && linesave[0] == "at")
                    {
                        if (!dictionaryMusic.ContainsKey(int.Parse(linesave[2])))
                            dictionaryMusic.Add(int.Parse(linesave[2]), int.Parse(linesave[7]));
                    }
                }
                packetTxtStream.Close();
            }

            foreach (FileInfo file in new DirectoryInfo(folderMap).GetFiles())
            {
                string name = "";
                int music = 0;
                if (dictionaryId.ContainsKey(int.Parse(file.Name)) && dictionaryIdLang.ContainsKey(dictionaryId[int.Parse(file.Name)]))
                    name = dictionaryIdLang[dictionaryId[int.Parse(file.Name)]];

                if (dictionaryMusic.ContainsKey(int.Parse(file.Name)))
                    music = dictionaryMusic[int.Parse(file.Name)];

                MapDTO map = new MapDTO
                {
                    Name = name,
                    Music = music,
                    MapId = short.Parse(file.Name),
                    Data = File.ReadAllBytes(file.FullName)
                };
                if (DAOFactory.MapDAO.LoadById(map.MapId) != null) continue; // Map already exists in list

                DAOFactory.MapDAO.Insert(map);
                i++;
            }

            Logger.Log.Info(string.Format(Language.Instance.GetMessageFromKey("MAPS_PARSED"), i));
        }

        public void ImportNpcs()
        {
            string fileNpcId = $"{_folder}\\monster.dat";
            string fileNpcLang = $"{_folder}\\_code_{System.Configuration.ConfigurationManager.AppSettings["language"]}_monster.txt";
            string filePacketTxt = $"{_folder}\\packet.txt";

            // store like this: (vnum, (name, level))
            Dictionary<int, KeyValuePair<string, short>> dictionaryNpcs = new Dictionary<int, KeyValuePair<string, short>>(); 
            Dictionary<string, string> dictionaryIdLang = new Dictionary<string, string>();
            Dictionary<int, int> dialog = new Dictionary<int, int>(); // unused (unfilled) variable

            string line;

            int vnum = -1;
            string name2 = "";
            bool itemAreaBegin = false;
            using (StreamReader npcIdStream = new StreamReader(fileNpcId, Encoding.GetEncoding(1252)))
            {
                while ((line = npcIdStream.ReadLine()) != null)
                {
                    string[] linesave = line.Split('\t');

                    if (linesave.Length > 2 && linesave[1] == "VNUM")
                    {
                        vnum = int.Parse(linesave[2]);
                        itemAreaBegin = true;
                    }
                    else if (linesave.Length > 2 && linesave[1] == "LEVEL")
                    {
                        if (!itemAreaBegin) continue;

                        dictionaryNpcs.Add(vnum, new KeyValuePair<string, short>(name2, short.Parse(linesave[2])));
                        // maybe set 'name2' and 'vnum' to default() for security?
                        itemAreaBegin = false;
                    }
                    else if (linesave.Length > 2 && linesave[1] == "NAME")
                    {
                        name2 = linesave[2];
                    }
                }
                npcIdStream.Close();
            }

            using (StreamReader npcIdLangStream = new StreamReader(fileNpcLang, Encoding.GetEncoding(1252)))
            {
                while ((line = npcIdLangStream.ReadLine()) != null)
                {
                    string[] linesave = line.Split('\t');
                    if (linesave.Length > 1 && !dictionaryIdLang.ContainsKey(linesave[0]))
                        dictionaryIdLang.Add(linesave[0], linesave[1]);
                }
                npcIdLangStream.Close();
            }


            int npcCounter = 0;
            short map = 0;
            short lastMap = 0; // unused variable

            using (StreamReader packetTxtStream = new StreamReader(filePacketTxt, Encoding.GetEncoding(1252)))
            {
                while ((line = packetTxtStream.ReadLine()) != null)
                {
                    string[] linesave = line.Split(' ');
                    if (linesave.Length > 5 && linesave[0] == "at")
                    {
                        lastMap = map;
                        map = short.Parse(linesave[2]);
                    }
                    else if (linesave.Length > 7 && linesave[0] == "in" && linesave[1] == "2")
                    {
                        try
                        {
                            if (long.Parse(linesave[3]) >= 10000)
                                continue; // dialog too high. but why?

                            int dialogNum = 0; // unused variable
                            if (dialog.ContainsKey(int.Parse(linesave[3])))
                                dialogNum = dialog[int.Parse(linesave[3])];

                            if (
                                DAOFactory.NpcDAO.LoadFromMap(map)
                                    .FirstOrDefault(
                                        s => s.MapId.Equals(map) && s.Vnum.Equals(short.Parse(linesave[2]))) != null)
                                continue; // Npc already existing

                            KeyValuePair<string, short> nameAndLevel = dictionaryNpcs[int.Parse(linesave[2])];
                            DAOFactory.NpcDAO.Insert(new NpcDTO
                            {
                                Vnum = short.Parse(linesave[2]),
                                Level = nameAndLevel.Value,
                                MapId = map,
                                MapX = short.Parse(linesave[4]),
                                MapY = short.Parse(linesave[5]),
                                Name = dictionaryIdLang[nameAndLevel.Key],
                                Position = short.Parse(linesave[6]),
                                Dialog = short.Parse(linesave[9])
                            });
                            npcCounter++;
                        }
                        catch (Exception)
                        {
                            // continue with next line in packet file
                        }
                    }
                }
                packetTxtStream.Close();
            }

            Logger.Log.Info(string.Format(Language.Instance.GetMessageFromKey("NPCS_PARSED"), npcCounter));
        }

        public void ImportPortals()
        {
            string filePacketTxt = $"{_folder}\\packet.txt";

            List<PortalDTO> listPacket = new List<PortalDTO>();
            List<PortalDTO> listPortal = new List<PortalDTO>();

            int portalCounter = 0;
            short map = 0;
            short lastMap = 0; // unused variable

            using (StreamReader packetTxtStream = new StreamReader(filePacketTxt, Encoding.GetEncoding(1252)))
            {
                string line;
                while ((line = packetTxtStream.ReadLine()) != null)
                {
                    string[] linesave = line.Split(' ');
                    if (linesave.Length > 5 && linesave[0] == "at")
                    {
                        lastMap = map;
                        map = short.Parse(linesave[2]);
                    }
                    else if (linesave.Length > 4 && linesave[0] == "gp")
                    {
                        short sourceX = short.Parse(linesave[1]);
                        short type = short.Parse(linesave[4]);
                        short sourceY = short.Parse(linesave[2]);
                        short destinationMapId = short.Parse(linesave[3]);

                        if (listPacket.FirstOrDefault(s => s.SourceMapId == map && s.SourceX == sourceX && s.SourceY == sourceY && s.DestinationMapId == destinationMapId) != null)
                            continue; // Portal already in list

                        listPacket.Add(new PortalDTO
                        {
                            SourceMapId = map,
                            SourceX = sourceX,
                            SourceY = sourceY,
                            DestinationMapId = destinationMapId,
                            Type = type,
                            DestinationX = -1,
                            DestinationY = -1,
                            IsDisabled = 0
                        });
                    }
                }
                packetTxtStream.Close();
            }

            listPacket = listPacket.OrderBy(s => s.SourceMapId).ThenBy(s => s.DestinationMapId).ThenBy(s => s.SourceY).ThenBy(s => s.SourceX).ToList();
            foreach (PortalDTO portal in listPacket)
            {
                // TODO Multiple portals (like Port Alveus <-> Nosville) wont be read properly?!

                PortalDTO p = listPacket.Except(listPortal).FirstOrDefault(s => s.SourceMapId.Equals(portal.DestinationMapId) && s.DestinationMapId.Equals(portal.SourceMapId));
                if (p == null) continue;

                portal.DestinationX = p.SourceX;
                portal.DestinationY = p.SourceY;
                p.DestinationY = portal.SourceY;
                p.DestinationX = portal.SourceX;
                listPortal.Add(p);
                listPortal.Add(portal);
            }

            // foreach portal in the new list of Portals
            // where none (=> !Any()) are found in the existing
            foreach (PortalDTO portal in listPortal.Where(portal => !DAOFactory.PortalDAO.LoadFromMap(portal.SourceMapId).Any(s => s.DestinationMapId.Equals(portal.DestinationMapId) && s.SourceX.Equals(portal.SourceX) && s.SourceY.Equals(portal.SourceY))))
            {
                // so this dude doesnt exist yet in DAOFactory -> insert it
                DAOFactory.PortalDAO.Insert(portal);
                portalCounter++;
            }

            Logger.Log.Info(string.Format(Language.Instance.GetMessageFromKey("PORTALS_PARSED"), portalCounter));
        }

        public void ImportShops()
        {
            string filePacketTxt = $"{_folder}\\packet.txt";

            Dictionary<int, int> dictionaryId = new Dictionary<int, int>();

            short lastMap = 0; // unused variable
            short currentMap = 0;
            int shopCounter = 0;

            using (StreamReader packetTxtStream = new StreamReader(filePacketTxt, Encoding.GetEncoding(1252)))
            {
                string line;
                while ((line = packetTxtStream.ReadLine()) != null)
                {
                    string[] linesave = line.Split(' ');
                    if (linesave.Length > 5 && linesave[0] == "at")
                    {
                        lastMap = currentMap;
                        currentMap = short.Parse(linesave[2]);
                    }
                    else if (linesave.Length > 7 && linesave[0] == "in" && linesave[1] == "2")
                    {
                        if (long.Parse(linesave[3]) >= 10000) continue;

                        NpcDTO npc = DAOFactory.NpcDAO.LoadFromMap(currentMap).FirstOrDefault(s => s.MapId.Equals(currentMap) && s.Vnum.Equals(short.Parse(linesave[2])));
                        if (npc == null) continue;

                        if (!dictionaryId.ContainsKey(short.Parse(linesave[3])))
                            dictionaryId.Add(short.Parse(linesave[3]), npc.NpcId);
                    }
                    else if (linesave.Length > 6 && linesave[0] == "shop" && linesave[1] == "2")
                    {
                        if (!dictionaryId.ContainsKey(short.Parse(linesave[2]))) continue;

                        string named = "";
                        for (int j = 6; j < linesave.Length; j++)
                        {
                            named += $"{linesave[j]} ";
                        }
                        named = named.Trim();

                        ShopDTO shop = new ShopDTO
                        {
                            Name = named,
                            NpcId = (short)dictionaryId[short.Parse(linesave[2])],
                            MenuType = short.Parse(linesave[4]),
                            ShopType = short.Parse(linesave[5])
                        };
                        if (DAOFactory.ShopDAO.LoadByNpc(shop.NpcId) == null)
                        {
                            DAOFactory.ShopDAO.Insert(shop);
                            shopCounter++;
                        }
                    }
                }
                packetTxtStream.Close();
            }

            Logger.Log.Info(string.Format(Language.Instance.GetMessageFromKey("SHOPS_PARSED"), shopCounter));
        }

        #endregion
    }
}
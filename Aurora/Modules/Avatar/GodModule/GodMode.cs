/*
 * Copyright (c) Contributors, http://aurora-sim.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using Aurora.Framework;
using Nini.Config;
using Nini.Ini;
using OpenMetaverse;
using OpenSim.Region.Framework.Interfaces;

namespace Aurora.Modules.Gods
{
    public class GodModifiers : ISharedRegionModule
    {
        #region Declares 

        //private static readonly ILog MainConsole.Instance = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly List<IScene> m_scenes = new List<IScene>();
        private bool m_Enabled = true;
        private IConfigSource m_config;
        private string m_oar_directory = "";

        #endregion

        #region ISharedRegionModule

        public void Initialise(IConfigSource source)
        {
            m_config = source;
            if (source.Configs["GodModule"] != null)
            {
                if (source.Configs["GodModule"].GetString(
                    "GodModule", Name) !=
                    Name)
                {
                    m_Enabled = false;
                    return;
                }
                m_oar_directory = source.Configs["GodModule"].GetString("DirectoryToSaveOARs", m_oar_directory);
            }
        }

        public void AddRegion(IScene scene)
        {
            if (!m_Enabled)
                return;

            m_scenes.Add(scene);

            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnClosingClient += OnClosingClient;
        }

        public void RemoveRegion(IScene scene)
        {
            if (!m_Enabled)
                return;

            m_scenes.Remove(scene);

            scene.EventManager.OnNewClient -= OnNewClient;
            scene.EventManager.OnClosingClient -= OnClosingClient;
        }

        public void RegionLoaded(IScene scene)
        {
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void PostInitialise()
        {
        }

        public string Name
        {
            get { return "AuroraGodModModule"; }
        }

        public void Close()
        {
        }

        #endregion

        #region Client

        private void OnNewClient(IClientAPI client)
        {
            client.OnGodUpdateRegionInfoUpdate += GodUpdateRegionInfoUpdate;
            client.OnGodlikeMessage += onGodlikeMessage;
            client.OnSaveState += GodSaveState;
        }

        private void OnClosingClient(IClientAPI client)
        {
            client.OnGodUpdateRegionInfoUpdate -= GodUpdateRegionInfoUpdate;
            client.OnGodlikeMessage -= onGodlikeMessage;
            client.OnSaveState -= GodSaveState;
        }

        /// <summary>
        ///   The user requested something from god tools
        /// </summary>
        /// <param name = "client"></param>
        /// <param name = "requester"></param>
        /// <param name = "Method"></param>
        /// <param name = "Parameter"></param>
        private void onGodlikeMessage(IClientAPI client, UUID requester, string Method, List<string> Parameter)
        {
            //Just rebuild the map
            if (Method == "refreshmapvisibility")
            {
                if (client.Scene.Permissions.IsGod(client.AgentId))
                {
                    //Rebuild the map tile
                    IMapImageGenerator mapModule = client.Scene.RequestModuleInterface<IMapImageGenerator>();
                    if(mapModule != null)
                        mapModule.CreateTerrainTexture();
                }
            }
        }

        /// <summary>
        ///   Save the state of the region
        /// </summary>
        /// <param name = "client"></param>
        /// <param name = "agentID"></param>
        public void GodSaveState(IClientAPI client, UUID agentID)
        {
            //Check for god perms
            if (client.Scene.Permissions.IsGod(client.AgentId))
            {
                IScene scene = MainConsole.Instance.ConsoleScene; //Switch back later
                MainConsole.Instance.RunCommand("change region " + client.Scene.RegionInfo.RegionName);
                MainConsole.Instance.RunCommand("save oar " + m_oar_directory + client.Scene.RegionInfo.RegionName +
                                                Util.UnixTimeSinceEpoch().ToString() + ".statesave.oar");
                if (scene == null)
                    MainConsole.Instance.RunCommand("change region root");
                else
                    MainConsole.Instance.RunCommand("change region " + scene.RegionInfo.RegionName);
            }
        }

        /// <summary>
        ///   The god has requested that we update something in the region configs
        /// </summary>
        /// <param name = "client"></param>
        /// <param name = "BillableFactor"></param>
        /// <param name = "PricePerMeter"></param>
        /// <param name = "EstateID"></param>
        /// <param name = "RegionFlags"></param>
        /// <param name = "SimName"></param>
        /// <param name = "RedirectX"></param>
        /// <param name = "RedirectY"></param>
        public void GodUpdateRegionInfoUpdate(IClientAPI client, float BillableFactor, int PricePerMeter, ulong EstateID,
                                              ulong RegionFlags, byte[] SimName, int RedirectX, int RedirectY)
        {
            //Check god perms
            if (!client.Scene.Permissions.IsGod(client.AgentId))
                return;

            //Update their current region with new information
            string oldRegionName = client.Scene.RegionInfo.RegionName;
            client.Scene.RegionInfo.RegionName = Utils.BytesToString(SimName);

            //Set the region loc X and Y
            if (RedirectX != 0)
                client.Scene.RegionInfo.RegionLocX = RedirectX*Constants.RegionSize;
            if (RedirectY != 0)
                client.Scene.RegionInfo.RegionLocY = RedirectY*Constants.RegionSize;

            //Update the estate ID
            if (client.Scene.RegionInfo.EstateSettings.EstateID != EstateID)
            {
                bool changed = DataManager.DataManager.RequestPlugin<IEstateConnector>().LinkRegion(
                            client.Scene.RegionInfo.RegionID, (int)EstateID);
                if (!changed)
                    client.SendAgentAlertMessage("Unable to connect to the given estate.", false);
                else
                {
                    client.Scene.RegionInfo.EstateSettings.EstateID = (uint)EstateID;
                    client.Scene.RegionInfo.EstateSettings.Save();
                }
            }

            //Set the other settings
            client.Scene.RegionInfo.EstateSettings.BillableFactor = BillableFactor;
            client.Scene.RegionInfo.EstateSettings.PricePerMeter = PricePerMeter;
            client.Scene.RegionInfo.EstateSettings.SetFromFlags(RegionFlags);

            client.Scene.RegionInfo.RegionSettings.AllowDamage = ((RegionFlags &
                                                                   (ulong) OpenMetaverse.RegionFlags.AllowDamage) ==
                                                                  (ulong) OpenMetaverse.RegionFlags.AllowDamage);
            client.Scene.RegionInfo.RegionSettings.FixedSun = ((RegionFlags & (ulong) OpenMetaverse.RegionFlags.SunFixed) ==
                                                               (ulong) OpenMetaverse.RegionFlags.SunFixed);
            client.Scene.RegionInfo.RegionSettings.BlockTerraform = ((RegionFlags &
                                                                      (ulong) OpenMetaverse.RegionFlags.BlockTerraform) ==
                                                                     (ulong) OpenMetaverse.RegionFlags.BlockTerraform);
            client.Scene.RegionInfo.RegionSettings.Sandbox = ((RegionFlags & (ulong) OpenMetaverse.RegionFlags.Sandbox) ==
                                                              (ulong) OpenMetaverse.RegionFlags.Sandbox);

            //Update skipping scripts/physics/collisions
            IEstateModule mod = client.Scene.RequestModuleInterface<IEstateModule>();
            if (mod != null)
                mod.SetSceneCoreDebug(
                    ((RegionFlags & (ulong) OpenMetaverse.RegionFlags.SkipScripts) ==
                     (ulong) OpenMetaverse.RegionFlags.SkipScripts),
                    ((RegionFlags & (ulong) OpenMetaverse.RegionFlags.SkipCollisions) ==
                     (ulong) OpenMetaverse.RegionFlags.SkipCollisions),
                    ((RegionFlags & (ulong) OpenMetaverse.RegionFlags.SkipPhysics) ==
                     (ulong) OpenMetaverse.RegionFlags.SkipPhysics));

            //Save the changes
            client.Scene.RegionInfo.EstateSettings.Save();
            client.Scene.RegionInfo.RegionSettings.Save();

            //Save the changes
            IConfig config = m_config.Configs["RegionStartup"];
            if (config != null)
            {
                //TERRIBLE! Needs to be modular, but we can't access the module from a scene module!
                if (config.GetString("Default") == "RegionLoaderDataBaseSystem")
                    SaveChangesDatabase(client.Scene.RegionInfo);
                else
                    SaveChangesFile(oldRegionName, client.Scene.RegionInfo);
            }
            else
                SaveChangesFile(oldRegionName, client.Scene.RegionInfo);


            //Tell the clients to update all references to the new settings
            foreach (IScenePresence sp in client.Scene.GetScenePresences())
            {
                HandleRegionInfoRequest(sp.ControllingClient, client.Scene);
            }

            //Update the grid server as well
            IGridRegisterModule gridRegisterModule = client.Scene.RequestModuleInterface<IGridRegisterModule>();
            if (gridRegisterModule != null)
                gridRegisterModule.UpdateGridRegion(client.Scene);
        }

        #endregion

        #region Helpers

        /// <summary>
        ///   Save the config files
        /// </summary>
        /// <param name = "OldRegionName"></param>
        /// <param name = "regionInfo"></param>
        private void SaveChangesFile(string OldRegionName, RegionInfo regionInfo)
        {
            string regionConfigPath = Path.Combine(Util.configDir(), "Regions");

            try
            {
                IConfig startupConfig = m_config.Configs["RegionStartup"];
                regionConfigPath = startupConfig.GetString("RegionsDirectory", regionConfigPath).Trim();
            }
            catch (Exception)
            {
                // No INI setting recorded.
            }
            if (!Directory.Exists(regionConfigPath))
                return;

            string[] iniFiles = Directory.GetFiles(regionConfigPath, "*.ini");
            int i = 0;
            foreach (string file in iniFiles)
            {
                IConfigSource source = new IniConfigSource(file, IniFileType.AuroraStyle);
                IConfig cnf = source.Configs[OldRegionName];
                if (cnf != null) //Does the old one exist in this file?
                {
                    IConfig check = source.Configs[regionInfo.RegionName];
                    if (check == null) //Is the new name non existant as well?
                    {
                        cnf.Set("Location",
                                (regionInfo.RegionLocX/Constants.RegionSize) + "," +
                                (regionInfo.RegionLocY/Constants.RegionSize));
                        cnf.Name = regionInfo.RegionName;
                        source.Save();
                    }
                    else
                    {
                        //The new region exists too, no name change
                        check.Set("Location",
                                  (regionInfo.RegionLocX/Constants.RegionSize) + "," +
                                  (regionInfo.RegionLocY/Constants.RegionSize));
                    }
                }
                i++;
            }
        }

        /// <summary>
        ///   Save the database configs
        /// </summary>
        /// <param name = "regionInfo"></param>
        private void SaveChangesDatabase(RegionInfo regionInfo)
        {
            IRegionInfoConnector connector = DataManager.DataManager.RequestPlugin<IRegionInfoConnector>();
            if (connector != null)
                connector.UpdateRegionInfo(regionInfo);
        }

        /// <summary>
        ///   Tell the client about the changes
        /// </summary>
        /// <param name = "remote_client"></param>
        /// <param name = "m_scene"></param>
        private void HandleRegionInfoRequest(IClientAPI remote_client, IScene m_scene)
        {
            RegionInfoForEstateMenuArgs args = new RegionInfoForEstateMenuArgs
                                                   {
                                                       billableFactor = m_scene.RegionInfo.EstateSettings.BillableFactor,
                                                       estateID = m_scene.RegionInfo.EstateSettings.EstateID,
                                                       maxAgents = (byte) m_scene.RegionInfo.RegionSettings.AgentLimit,
                                                       objectBonusFactor =
                                                           (float) m_scene.RegionInfo.RegionSettings.ObjectBonus,
                                                       parentEstateID = m_scene.RegionInfo.EstateSettings.ParentEstateID,
                                                       pricePerMeter = m_scene.RegionInfo.EstateSettings.PricePerMeter,
                                                       redirectGridX = m_scene.RegionInfo.EstateSettings.RedirectGridX,
                                                       redirectGridY = m_scene.RegionInfo.EstateSettings.RedirectGridY
                                                   };

            IEstateModule estate = m_scene.RequestModuleInterface<IEstateModule>();
            args.regionFlags = estate == null ? 0 : (uint)estate.GetRegionFlags();

            args.simAccess = m_scene.RegionInfo.AccessLevel;
            args.sunHour = (float) m_scene.RegionInfo.RegionSettings.SunPosition;
            args.terrainLowerLimit = (float) m_scene.RegionInfo.RegionSettings.TerrainLowerLimit;
            args.terrainRaiseLimit = (float) m_scene.RegionInfo.RegionSettings.TerrainRaiseLimit;
            args.useEstateSun = m_scene.RegionInfo.RegionSettings.UseEstateSun;
            args.waterHeight = (float) m_scene.RegionInfo.RegionSettings.WaterHeight;
            args.simName = m_scene.RegionInfo.RegionName;
            args.regionType = m_scene.RegionInfo.RegionType;

            remote_client.SendRegionInfoToEstateMenu(args);
        }

        #endregion
    }
}
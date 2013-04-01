/*
 * Copyright (c) Contributors, http://aurora-sim.org/, http://opensimulator.org/
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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using OpenMetaverse;
using Aurora.Framework;
using OpenSim.Services.Interfaces;

namespace OpenSim.Region.Framework.Scenes.Serialization
{
    /// <summary>
    ///   Serialize and deserialize scene objects.
    /// </summary>
    /// This should really be in Aurora.Framework.Serialization but this would mean circular dependency problems
    /// right now - hopefully this isn't forever.
    public class SceneObjectSerializer
    {
        /// <summary>
        ///   Restore this part from the serialized xml representation.
        /// </summary>
        /// <param name = "fromUserInventoryItemId">The inventory id from which this part came, if applicable</param>
        /// <param name = "xmlReader"></param>
        /// <returns></returns>
        public static SceneObjectPart FromXml(XmlTextReader xmlReader, IRegistryCore scene)
        {
            SceneObjectPart part = Xml2ToSOP(xmlReader, scene);

            // for tempOnRez objects, we have to fix the Expire date.
            if ((part.Flags & PrimFlags.TemporaryOnRez) != 0) part.ResetExpire();
            part.GenerateRotationalVelocityFromOmega(); //Fix the rotational velocity
            return part;
        }

        /// <summary>
        ///   Deserialize a scene object from the original xml format
        /// </summary>
        /// <param name = "serialization"></param>
        /// <returns></returns>
        public static SceneObjectGroup FromOriginalXmlFormat(string serialization, IRegistryCore scene)
        {
            return FromOriginalXmlFormat(UUID.Zero, serialization, scene);
        }

        /// <summary>
        ///   Deserialize a scene object from the original xml format
        /// </summary>
        /// <param name = "serialization"></param>
        /// <returns></returns>
        public static SceneObjectGroup FromOriginalXmlFormat(UUID fromUserInventoryItemID, string xmlData,
                                                             IRegistryCore scene)
        {
            //MainConsole.Instance.DebugFormat("[SOG]: Starting deserialization of SOG");
            //int time = Util.EnvironmentTickCount();

            try
            {
                StringReader sr;
                XmlTextReader reader;
                XmlNodeList parts;
                XmlDocument doc;

                doc = new XmlDocument();
                doc.LoadXml(xmlData);
                parts = doc.GetElementsByTagName("RootPart");

                IScene m_sceneForGroup = scene is IScene ? (IScene) scene : null;
                SceneObjectGroup sceneObject;
                if (parts.Count == 0)
                {
                    sceneObject = FromXml2Format(xmlData, m_sceneForGroup);
                    if (sceneObject == null)
                        throw new Exception("Invalid Xml format - no root part");
                    else
                        return sceneObject;
                }

                sr = new StringReader(parts[0].InnerXml);
                reader = new XmlTextReader(sr);


                sceneObject = new SceneObjectGroup(FromXml(reader, scene), m_sceneForGroup, false);
                sceneObject.RootPart.FromUserInventoryItemID = fromUserInventoryItemID;
                reader.Close();
                sr.Close();

                parts = doc.GetElementsByTagName("Part");

                for (int i = 0; i < parts.Count; i++)
                {
                    sr = new StringReader(parts[i].InnerXml);
                    reader = new XmlTextReader(sr);
                    SceneObjectPart part = FromXml(reader, scene);
                    sceneObject.AddChild(part, part.LinkNum);
                    part.TrimPermissions();
                    part.StoreUndoState();
                    reader.Close();
                    sr.Close();
                }

                return sceneObject;
            }
            catch (Exception e)
            {
                MainConsole.Instance.ErrorFormat(
                    "[SERIALIZER]: Deserialization of xml failed with {0}.  xml was {1}", e, xmlData);
                return null;
            }
        }


        /// <summary>
        ///   Serialize a scene object to the original xml format
        /// </summary>
        /// <param name = "sceneObject"></param>
        /// <returns></returns>
        public static string ToOriginalXmlFormat(SceneObjectGroup sceneObject)
        {
            using (StringWriter sw = new StringWriter())
            {
                using (XmlTextWriter writer = new XmlTextWriter(sw))
                {
                    ToOriginalXmlFormat(sceneObject, writer);
                }

                return sw.ToString();
            }
        }

        /// <summary>
        ///   Serialize a scene object to the original xml format
        /// </summary>
        /// <param name = "sceneObject"></param>
        /// <returns></returns>
        public static void ToOriginalXmlFormat(SceneObjectGroup sceneObject, XmlTextWriter writer)
        {
            //MainConsole.Instance.DebugFormat("[SERIALIZER]: Starting serialization of {0}", Name);
            //int time = Util.EnvironmentTickCount();

            writer.WriteStartElement(String.Empty, "SceneObjectGroup", String.Empty);
            writer.WriteStartElement(String.Empty, "RootPart", String.Empty);
            ToXmlFormat(sceneObject.RootPart, writer);
            writer.WriteEndElement();
            writer.WriteStartElement(String.Empty, "OtherParts", String.Empty);

            SceneObjectPart[] parts = sceneObject.Parts;
#if (!ISWIN)
            foreach (SceneObjectPart part in parts)
            {
                if (part.UUID != sceneObject.RootPart.UUID)
                {
                    writer.WriteStartElement(String.Empty, "Part", String.Empty);
                    ToXmlFormat(part, writer);
                    writer.WriteEndElement();
                }
            }
#else
            foreach (SceneObjectPart part in parts.Where(part => part.UUID != sceneObject.RootPart.UUID))
            {
                writer.WriteStartElement(String.Empty, "Part", String.Empty);
                ToXmlFormat(part, writer);
                writer.WriteEndElement();
            }
#endif

            writer.WriteEndElement(); // OtherParts
            writer.WriteEndElement(); // SceneObjectGroup

            //MainConsole.Instance.DebugFormat("[SERIALIZER]: Finished serialization of SOG {0}, {1}ms", Name, Util.EnvironmentTickCount() - time);
        }

        protected static void ToXmlFormat(SceneObjectPart part, XmlTextWriter writer)
        {
            SOPToXml2(writer, part, new Dictionary<string, object>());
        }

        public static SceneObjectGroup FromXml2Format(string xmlData, IScene scene)
        {
            //MainConsole.Instance.DebugFormat("[SOG]: Starting deserialization of SOG");
            //int time = Util.EnvironmentTickCount();

            XmlDocument doc = new XmlDocument();
            try
            {
                doc.LoadXml(xmlData);
                SceneObjectGroup grp = InternalFromXml2Format(doc, scene);
                xmlData = null;
                return grp;
            }
            catch (Exception e)
            {
                MainConsole.Instance.ErrorFormat("[SERIALIZER]: Deserialization of xml failed with {0}.  xml was {1}", e, xmlData);
                return null;
            }
            finally
            {
                doc.RemoveAll();
                doc = null;
            }
        }

        public static SceneObjectGroup FromXml2Format(ref MemoryStream ms, IScene scene)
        {
            //MainConsole.Instance.DebugFormat("[SOG]: Starting deserialization of SOG");
            //int time = Util.EnvironmentTickCount();

            XmlDocument doc = new XmlDocument();
            SceneObjectGroup grp = null;
            try
            {
                doc.Load(ms);

                grp = InternalFromXml2Format(doc, scene);
                foreach(var c in grp.ChildrenList)
                    c.FinishedSerializingGenericProperties();
                return grp;
            }
            catch (Exception e)
            {
                MainConsole.Instance.ErrorFormat("[SERIALIZER]: Deserialization of xml failed with {0}", e);
                return grp;
            }
            finally
            {
                doc = null;
            }
        }

        private static SceneObjectGroup InternalFromXml2Format(XmlDocument doc, IScene scene)
        {
            //MainConsole.Instance.DebugFormat("[SOG]: Starting deserialization of SOG");
            //int time = Util.EnvironmentTickCount();

            try
            {
                XmlNodeList parts = doc.GetElementsByTagName("SceneObjectPart");
                if (parts.Count == 0)
                {
                    MainConsole.Instance.ErrorFormat(
                        "[SERIALIZER]: Deserialization of xml failed: No SceneObjectPart nodes. xml was " + doc.Value);
                    return null;
                }

                StringReader sr = new StringReader(parts[0].OuterXml);
                XmlTextReader reader = new XmlTextReader(sr);
                SceneObjectGroup sceneObject = new SceneObjectGroup(FromXml(reader, scene), scene);
                reader.Close();
                sr.Close();

                // Then deal with the rest
                for (int i = 1; i < parts.Count; i++)
                {
                    sr = new StringReader(parts[i].OuterXml);
                    reader = new XmlTextReader(sr);
                    SceneObjectPart part = FromXml(reader, scene);

                    sceneObject.AddChild(part, part.LinkNum);

                    part.StoreUndoState();
                    reader.Close();
                    sr.Close();
                }
                parts = null;
                doc = null;
                return sceneObject;
            }
            catch (Exception e)
            {
                MainConsole.Instance.ErrorFormat("[SERIALIZER]: Deserialization of xml failed with {0}.  xml was {1}", e, doc.Value);
                return null;
            }
        }

        /// <summary>
        ///   Serialize a scene object to the 'xml2' format.
        /// </summary>
        /// <param name = "sceneObject"></param>
        /// <returns></returns>
        public static string ToXml2Format(SceneObjectGroup sceneObject)
        {
            using (StringWriter sw = new StringWriter())
            {
                using (XmlTextWriter writer = new XmlTextWriter(sw))
                {
                    SOGToXml2(writer, sceneObject, new Dictionary<string, object>());
                    writer.Close();
                }

                string str = sw.ToString();
                sw.Flush();
                sw.Close();
                return str;
            }
        }

        /// <summary>
        ///   Serialize a scene object to the 'xml2' format.
        /// </summary>
        /// <param name = "sceneObject"></param>
        /// <returns></returns>
        public static byte[] ToBinaryXml2Format(SceneObjectGroup sceneObject)
        {
            using (MemoryStream sw = new MemoryStream())
            {
                using (StreamWriter wr = new StreamWriter(sw, Encoding.UTF8))
                {
                    using (XmlTextWriter writer = new XmlTextWriter(wr))
                    {
                        SOGToXml2(writer, sceneObject, new Dictionary<string, object>());
                    }
                    return sw.ToArray();
                }
            }
        }

        #region manual serialization

        #region Delegates

        public delegate void SOPXmlProcessor(SceneObjectPart sop, XmlTextReader reader);

        #endregion

        private static readonly Dictionary<string, SOPXmlProcessor> m_SOPXmlProcessors =
            new Dictionary<string, SOPXmlProcessor>();

        private static readonly Dictionary<string, TaskInventoryXmlProcessor> m_TaskInventoryXmlProcessors =
            new Dictionary<string, TaskInventoryXmlProcessor>();

        private static readonly Dictionary<string, ShapeXmlProcessor> m_ShapeXmlProcessors =
            new Dictionary<string, ShapeXmlProcessor>();

        private static readonly Dictionary<string, Serialization> m_genericSerializers =
            new Dictionary<string, Serialization>();

        static SceneObjectSerializer()
        {
            #region SOPXmlProcessors initialization

            m_SOPXmlProcessors.Add("AllowedDrop", ProcessAllowedDrop);
            m_SOPXmlProcessors.Add("CreatorID", ProcessCreatorID);
            m_SOPXmlProcessors.Add("CreatorData", ProcessCreatorData);
            m_SOPXmlProcessors.Add("InventorySerial", ProcessInventorySerial);
            m_SOPXmlProcessors.Add("TaskInventory", ProcessTaskInventory);
            m_SOPXmlProcessors.Add("UUID", ProcessUUID);
            m_SOPXmlProcessors.Add("LocalId", ProcessLocalId);
            m_SOPXmlProcessors.Add("Name", ProcessName);
            m_SOPXmlProcessors.Add("Material", ProcessMaterial);
            m_SOPXmlProcessors.Add("PassTouch", ProcessPassTouch);
            m_SOPXmlProcessors.Add("PassCollisions", ProcessPassCollisions);
            m_SOPXmlProcessors.Add("ScriptAccessPin", ProcessScriptAccessPin);
            m_SOPXmlProcessors.Add("GroupPosition", ProcessGroupPosition);
            m_SOPXmlProcessors.Add("OffsetPosition", ProcessOffsetPosition);
            m_SOPXmlProcessors.Add("RotationOffset", ProcessRotationOffset);
            m_SOPXmlProcessors.Add("Velocity", ProcessVelocity);
            //m_SOPXmlProcessors.Add("AngularVelocity", ProcessAngularVelocity);
            m_SOPXmlProcessors.Add("Acceleration", ProcessAcceleration);
            m_SOPXmlProcessors.Add("Description", ProcessDescription);
            m_SOPXmlProcessors.Add("Color", ProcessColor);
            m_SOPXmlProcessors.Add("Text", ProcessText);
            m_SOPXmlProcessors.Add("SitName", ProcessSitName);
            m_SOPXmlProcessors.Add("TouchName", ProcessTouchName);
            m_SOPXmlProcessors.Add("LinkNum", ProcessLinkNum);
            m_SOPXmlProcessors.Add("ClickAction", ProcessClickAction);
            m_SOPXmlProcessors.Add("Shape", ProcessShape);
            m_SOPXmlProcessors.Add("Scale", ProcessScale);
            m_SOPXmlProcessors.Add("UpdateFlag", ProcessUpdateFlag);
            m_SOPXmlProcessors.Add("SitTargetOrientation", ProcessSitTargetOrientation);
            m_SOPXmlProcessors.Add("SitTargetPosition", ProcessSitTargetPosition);
            m_SOPXmlProcessors.Add("SitTargetPositionLL", ProcessSitTargetPositionLL);
            m_SOPXmlProcessors.Add("SitTargetOrientationLL", ProcessSitTargetOrientationLL);
            m_SOPXmlProcessors.Add("ParentID", ProcessParentID);
            m_SOPXmlProcessors.Add("CreationDate", ProcessCreationDate);
            m_SOPXmlProcessors.Add("Category", ProcessCategory);
            m_SOPXmlProcessors.Add("SalePrice", ProcessSalePrice);
            m_SOPXmlProcessors.Add("ObjectSaleType", ProcessObjectSaleType);
            m_SOPXmlProcessors.Add("OwnershipCost", ProcessOwnershipCost);
            m_SOPXmlProcessors.Add("GroupID", ProcessGroupID);
            m_SOPXmlProcessors.Add("OwnerID", ProcessOwnerID);
            m_SOPXmlProcessors.Add("LastOwnerID", ProcessLastOwnerID);
            m_SOPXmlProcessors.Add("BaseMask", ProcessBaseMask);
            m_SOPXmlProcessors.Add("OwnerMask", ProcessOwnerMask);
            m_SOPXmlProcessors.Add("GroupMask", ProcessGroupMask);
            m_SOPXmlProcessors.Add("EveryoneMask", ProcessEveryoneMask);
            m_SOPXmlProcessors.Add("NextOwnerMask", ProcessNextOwnerMask);
            m_SOPXmlProcessors.Add("Flags", ProcessFlags);
            m_SOPXmlProcessors.Add("CollisionSound", ProcessCollisionSound);
            m_SOPXmlProcessors.Add("CollisionSoundVolume", ProcessCollisionSoundVolume);
            m_SOPXmlProcessors.Add("MediaUrl", ProcessMediaUrl);
            m_SOPXmlProcessors.Add("TextureAnimation", ProcessTextureAnimation);
            m_SOPXmlProcessors.Add("ParticleSystem", ProcessParticleSystem);
            m_SOPXmlProcessors.Add("PayPrice0", ProcessPayPrice0);
            m_SOPXmlProcessors.Add("PayPrice1", ProcessPayPrice1);
            m_SOPXmlProcessors.Add("PayPrice2", ProcessPayPrice2);
            m_SOPXmlProcessors.Add("PayPrice3", ProcessPayPrice3);
            m_SOPXmlProcessors.Add("PayPrice4", ProcessPayPrice4);

            #endregion

            #region TaskInventoryXmlProcessors initialization

            m_TaskInventoryXmlProcessors.Add("AssetID", ProcessTIAssetID);
            m_TaskInventoryXmlProcessors.Add("BasePermissions", ProcessTIBasePermissions);
            m_TaskInventoryXmlProcessors.Add("CreationDate", ProcessTICreationDate);
            m_TaskInventoryXmlProcessors.Add("CreatorID", ProcessTICreatorID);
            m_TaskInventoryXmlProcessors.Add("CreatorData", ProcessTICreatorData);
            m_TaskInventoryXmlProcessors.Add("Description", ProcessTIDescription);
            m_TaskInventoryXmlProcessors.Add("EveryonePermissions", ProcessTIEveryonePermissions);
            m_TaskInventoryXmlProcessors.Add("Flags", ProcessTIFlags);
            m_TaskInventoryXmlProcessors.Add("GroupID", ProcessTIGroupID);
            m_TaskInventoryXmlProcessors.Add("GroupPermissions", ProcessTIGroupPermissions);
            m_TaskInventoryXmlProcessors.Add("InvType", ProcessTIInvType);
            m_TaskInventoryXmlProcessors.Add("ItemID", ProcessTIItemID);
            m_TaskInventoryXmlProcessors.Add("OldItemID", ProcessTIOldItemID);
            m_TaskInventoryXmlProcessors.Add("LastOwnerID", ProcessTILastOwnerID);
            m_TaskInventoryXmlProcessors.Add("Name", ProcessTIName);
            m_TaskInventoryXmlProcessors.Add("NextPermissions", ProcessTINextPermissions);
            m_TaskInventoryXmlProcessors.Add("OwnerID", ProcessTIOwnerID);
            m_TaskInventoryXmlProcessors.Add("CurrentPermissions", ProcessTICurrentPermissions);
            m_TaskInventoryXmlProcessors.Add("ParentID", ProcessTIParentID);
            m_TaskInventoryXmlProcessors.Add("ParentPartID", ProcessTIParentPartID);
            m_TaskInventoryXmlProcessors.Add("PermsGranter", ProcessTIPermsGranter);
            m_TaskInventoryXmlProcessors.Add("PermsMask", ProcessTIPermsMask);
            m_TaskInventoryXmlProcessors.Add("Type", ProcessTIType);
            m_TaskInventoryXmlProcessors.Add("OwnerChanged", ProcessTIOwnerChanged);

            #endregion

            #region ShapeXmlProcessors initialization

            m_ShapeXmlProcessors.Add("ProfileCurve", ProcessShpProfileCurve);
            m_ShapeXmlProcessors.Add("TextureEntry", ProcessShpTextureEntry);
            m_ShapeXmlProcessors.Add("ExtraParams", ProcessShpExtraParams);
            m_ShapeXmlProcessors.Add("PathBegin", ProcessShpPathBegin);
            m_ShapeXmlProcessors.Add("PathCurve", ProcessShpPathCurve);
            m_ShapeXmlProcessors.Add("PathEnd", ProcessShpPathEnd);
            m_ShapeXmlProcessors.Add("PathRadiusOffset", ProcessShpPathRadiusOffset);
            m_ShapeXmlProcessors.Add("PathRevolutions", ProcessShpPathRevolutions);
            m_ShapeXmlProcessors.Add("PathScaleX", ProcessShpPathScaleX);
            m_ShapeXmlProcessors.Add("PathScaleY", ProcessShpPathScaleY);
            m_ShapeXmlProcessors.Add("PathShearX", ProcessShpPathShearX);
            m_ShapeXmlProcessors.Add("PathShearY", ProcessShpPathShearY);
            m_ShapeXmlProcessors.Add("PathSkew", ProcessShpPathSkew);
            m_ShapeXmlProcessors.Add("PathTaperX", ProcessShpPathTaperX);
            m_ShapeXmlProcessors.Add("PathTaperY", ProcessShpPathTaperY);
            m_ShapeXmlProcessors.Add("PathTwist", ProcessShpPathTwist);
            m_ShapeXmlProcessors.Add("PathTwistBegin", ProcessShpPathTwistBegin);
            m_ShapeXmlProcessors.Add("PCode", ProcessShpPCode);
            m_ShapeXmlProcessors.Add("ProfileBegin", ProcessShpProfileBegin);
            m_ShapeXmlProcessors.Add("ProfileEnd", ProcessShpProfileEnd);
            m_ShapeXmlProcessors.Add("ProfileHollow", ProcessShpProfileHollow);
            m_ShapeXmlProcessors.Add("Scale", ProcessShpScale);
            m_ShapeXmlProcessors.Add("State", ProcessShpState);
            m_ShapeXmlProcessors.Add("ProfileShape", ProcessShpProfileShape);
            m_ShapeXmlProcessors.Add("HollowShape", ProcessShpHollowShape);
            m_ShapeXmlProcessors.Add("SculptTexture", ProcessShpSculptTexture);
            m_ShapeXmlProcessors.Add("SculptType", ProcessShpSculptType);
            m_ShapeXmlProcessors.Add("SculptData", ProcessShpSculptData);
            m_ShapeXmlProcessors.Add("FlexiSoftness", ProcessShpFlexiSoftness);
            m_ShapeXmlProcessors.Add("FlexiTension", ProcessShpFlexiTension);
            m_ShapeXmlProcessors.Add("FlexiDrag", ProcessShpFlexiDrag);
            m_ShapeXmlProcessors.Add("FlexiGravity", ProcessShpFlexiGravity);
            m_ShapeXmlProcessors.Add("FlexiWind", ProcessShpFlexiWind);
            m_ShapeXmlProcessors.Add("FlexiForceX", ProcessShpFlexiForceX);
            m_ShapeXmlProcessors.Add("FlexiForceY", ProcessShpFlexiForceY);
            m_ShapeXmlProcessors.Add("FlexiForceZ", ProcessShpFlexiForceZ);
            m_ShapeXmlProcessors.Add("LightColorR", ProcessShpLightColorR);
            m_ShapeXmlProcessors.Add("LightColorG", ProcessShpLightColorG);
            m_ShapeXmlProcessors.Add("LightColorB", ProcessShpLightColorB);
            m_ShapeXmlProcessors.Add("LightColorA", ProcessShpLightColorA);
            m_ShapeXmlProcessors.Add("LightRadius", ProcessShpLightRadius);
            m_ShapeXmlProcessors.Add("LightCutoff", ProcessShpLightCutoff);
            m_ShapeXmlProcessors.Add("LightFalloff", ProcessShpLightFalloff);
            m_ShapeXmlProcessors.Add("LightIntensity", ProcessShpLightIntensity);
            m_ShapeXmlProcessors.Add("FlexiEntry", ProcessShpFlexiEntry);
            m_ShapeXmlProcessors.Add("LightEntry", ProcessShpLightEntry);
            m_ShapeXmlProcessors.Add("SculptEntry", ProcessShpSculptEntry);
            m_ShapeXmlProcessors.Add("Media", ProcessShpMedia);

            #endregion
        }

        public static void AddSerializer(string Name, ISOPSerializerModule processor)
        {
            if (!m_SOPXmlProcessors.ContainsKey(Name))
            {
                m_SOPXmlProcessors.Add(Name, processor.Deserialization);
                m_genericSerializers.Add(Name, processor.Serialization);
            }
            else
                MainConsole.Instance.Warn("[SCENEOBJECTSERIALIZER]: Tried to add an additional SOP processor for " + Name);
        }

        public static void RemoveSerializer(string Name)
        {
            if (m_SOPXmlProcessors.ContainsKey(Name))
            {
                m_SOPXmlProcessors.Remove(Name);
                m_genericSerializers.Remove(Name);
            }
            else
                MainConsole.Instance.Warn("[SCENEOBJECTSERIALIZER]: Tried to remove a SOP processor for " + Name +
                           " that did not exist");
        }

        ////////// Write /////////

        public static void SOGToXml2(XmlTextWriter writer, SceneObjectGroup sog, Dictionary<string, object> options)
        {
            writer.WriteStartElement(String.Empty, "SceneObjectGroup", String.Empty);
            SOPToXml2(writer, sog.RootPart, options);
            writer.WriteStartElement(String.Empty, "OtherParts", String.Empty);

            sog.ForEachPart(delegate(SceneObjectPart sop)
                                {
                                    if (sop.UUID != sog.RootPart.UUID)
                                        SOPToXml2(writer, sop, options);
                                });

            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        public static void SOPToXml2(XmlTextWriter writer, SceneObjectPart sop, Dictionary<string, object> options)
        {
            writer.WriteStartElement("SceneObjectPart");
            writer.WriteAttributeString("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
            writer.WriteAttributeString("xmlns:xsd", "http://www.w3.org/2001/XMLSchema");

            writer.WriteElementString("AllowedDrop", sop.AllowedDrop.ToString().ToLower());
            WriteUUID(writer, "CreatorID", sop.CreatorID, options);

            if (!string.IsNullOrEmpty(sop.CreatorData))
                writer.WriteElementString("CreatorData", sop.CreatorData);
           /* else if (options.ContainsKey("profile"))
            {
                IUserFinder m_UserManagement = sop.ParentEntity.Scene.RequestModuleInterface<IUserFinder>();
                string name = m_UserManagement.GetUserName(sop.CreatorID);
                writer.WriteElementString("CreatorData", (string) options["profile"] + "/" + sop.CreatorID + ";" + name);
            }
            */

            writer.WriteElementString("InventorySerial", sop.InventorySerial.ToString());

            WriteTaskInventory(writer, sop.TaskInventory, options, sop.ParentEntity.Scene);

            WriteUUID(writer, "UUID", sop.UUID, options);
            writer.WriteElementString("LocalId", sop.LocalId.ToString());
            writer.WriteElementString("Name", sop.Name);
            writer.WriteElementString("Material", sop.Material.ToString());
            writer.WriteElementString("PassTouch", sop.PassTouch.ToString());
            writer.WriteElementString("PassCollisions", sop.PassCollisions.ToString());
            writer.WriteElementString("ScriptAccessPin", sop.ScriptAccessPin.ToString());

            WriteVector(writer, "GroupPosition", sop.GroupPosition);
            WriteVector(writer, "OffsetPosition", sop.OffsetPosition);

            WriteQuaternion(writer, "RotationOffset", sop.RotationOffset);
            WriteVector(writer, "Velocity", sop.Velocity);
            WriteVector(writer, "AngularVelocity", sop.AngularVelocity);
            WriteVector(writer, "Acceleration", sop.Acceleration);
            writer.WriteElementString("Description", sop.Description);
            writer.WriteStartElement("Color");
            writer.WriteElementString("R", sop.Color.R.ToString(Utils.EnUsCulture));
            writer.WriteElementString("G", sop.Color.G.ToString(Utils.EnUsCulture));
            writer.WriteElementString("B", sop.Color.B.ToString(Utils.EnUsCulture));
            writer.WriteElementString("A", sop.Color.G.ToString(Utils.EnUsCulture));
            writer.WriteEndElement();

            writer.WriteElementString("Text", sop.Text);
            writer.WriteElementString("SitName", sop.SitName);
            writer.WriteElementString("TouchName", sop.TouchName);

            writer.WriteElementString("LinkNum", sop.LinkNum.ToString());
            writer.WriteElementString("ClickAction", sop.ClickAction.ToString());

            WriteShape(writer, sop.Shape, options);

            WriteVector(writer, "Scale", sop.Scale);
            writer.WriteElementString("UpdateFlag", ((byte) 0).ToString());
            WriteQuaternion(writer, "SitTargetOrientation", sop.SitTargetOrientation);
            WriteVector(writer, "SitTargetPosition", sop.SitTargetPosition);
            WriteVector(writer, "SitTargetPositionLL", sop.SitTargetPositionLL);
            WriteQuaternion(writer, "SitTargetOrientationLL", sop.SitTargetOrientationLL);
            writer.WriteElementString("ParentID", sop.ParentID.ToString());
            writer.WriteElementString("CreationDate", sop.CreationDate.ToString());
            writer.WriteElementString("Category", sop.Category.ToString());
            writer.WriteElementString("SalePrice", sop.SalePrice.ToString());
            writer.WriteElementString("ObjectSaleType", sop.ObjectSaleType.ToString());
            writer.WriteElementString("OwnershipCost", sop.OwnershipCost.ToString());
            WriteUUID(writer, "GroupID", sop.GroupID, options);
            WriteUUID(writer, "OwnerID", sop.OwnerID, options);
            WriteUUID(writer, "LastOwnerID", sop.LastOwnerID, options);
            writer.WriteElementString("BaseMask", sop.BaseMask.ToString());
            writer.WriteElementString("OwnerMask", sop.OwnerMask.ToString());
            writer.WriteElementString("GroupMask", sop.GroupMask.ToString());
            writer.WriteElementString("EveryoneMask", sop.EveryoneMask.ToString());
            writer.WriteElementString("NextOwnerMask", sop.NextOwnerMask.ToString());
            writer.WriteElementString("Flags", sop.Flags.ToString());
            WriteUUID(writer, "CollisionSound", sop.CollisionSound, options);
            writer.WriteElementString("CollisionSoundVolume", sop.CollisionSoundVolume.ToString());
            if (sop.MediaUrl != null)
                writer.WriteElementString("MediaUrl", sop.MediaUrl);
            WriteBytes(writer, "TextureAnimation", sop.TextureAnimation);
            WriteBytes(writer, "ParticleSystem", sop.ParticleSystem);
            writer.WriteElementString("PayPrice0", sop.PayPrice[0].ToString());
            writer.WriteElementString("PayPrice1", sop.PayPrice[1].ToString());
            writer.WriteElementString("PayPrice2", sop.PayPrice[2].ToString());
            writer.WriteElementString("PayPrice3", sop.PayPrice[3].ToString());
            writer.WriteElementString("PayPrice4", sop.PayPrice[4].ToString());


            //Write the generic elements last
            foreach (KeyValuePair<string, Serialization> kvp in m_genericSerializers)
            {
                writer.WriteElementString(kvp.Key, kvp.Value(sop));
            }

            writer.WriteEndElement();
        }

        private static void WriteUUID(XmlTextWriter writer, string name, UUID id, Dictionary<string, object> options)
        {
            writer.WriteStartElement(name);
            writer.WriteElementString(options.ContainsKey("old-guids") ? "Guid" : "UUID", id.ToString());
            writer.WriteEndElement();
        }

        private static void WriteVector(XmlTextWriter writer, string name, Vector3 vec)
        {
            writer.WriteStartElement(name);
            writer.WriteElementString("X", vec.X.ToString(Utils.EnUsCulture));
            writer.WriteElementString("Y", vec.Y.ToString(Utils.EnUsCulture));
            writer.WriteElementString("Z", vec.Z.ToString(Utils.EnUsCulture));
            writer.WriteEndElement();
        }

        private static void WriteQuaternion(XmlTextWriter writer, string name, Quaternion quat)
        {
            writer.WriteStartElement(name);
            writer.WriteElementString("X", quat.X.ToString(Utils.EnUsCulture));
            writer.WriteElementString("Y", quat.Y.ToString(Utils.EnUsCulture));
            writer.WriteElementString("Z", quat.Z.ToString(Utils.EnUsCulture));
            writer.WriteElementString("W", quat.W.ToString(Utils.EnUsCulture));
            writer.WriteEndElement();
        }

        private static void WriteBytes(XmlTextWriter writer, string name, byte[] data)
        {
            writer.WriteStartElement(name);
            byte[] d;
            if (data != null)
                d = data;
            else
                d = Utils.EmptyBytes;
            writer.WriteBase64(d, 0, d.Length);
            writer.WriteEndElement(); // name
        }

        private static void WriteTaskInventory(XmlTextWriter writer, TaskInventoryDictionary tinv,
                                               Dictionary<string, object> options, IScene scene)
        {
            if (tinv.Count > 0) // otherwise skip this
            {
                writer.WriteStartElement("TaskInventory");

                foreach (TaskInventoryItem item in tinv.Values)
                {
                    writer.WriteStartElement("TaskInventoryItem");

                    WriteUUID(writer, "AssetID", item.AssetID, options);
                    writer.WriteElementString("BasePermissions", item.BasePermissions.ToString());
                    writer.WriteElementString("CreationDate", item.CreationDate.ToString());
                    WriteUUID(writer, "CreatorID", item.CreatorID, options);
                    if (!string.IsNullOrEmpty(item.CreatorData))
                        writer.WriteElementString("CreatorData", item.CreatorData);
                    /*else if (options.ContainsKey("profile"))
                    {
                        IUserFinder m_UserManagement = scene.RequestModuleInterface<IUserFinder>();
                        string name = m_UserManagement.GetUserName(item.CreatorID);
                        writer.WriteElementString("CreatorData",
                                                  (string) options["profile"] + "/" + item.CreatorID + ";" + name);
                    }
                     */
                    writer.WriteElementString("Description", item.Description);
                    writer.WriteElementString("EveryonePermissions", item.EveryonePermissions.ToString());
                    writer.WriteElementString("Flags", item.Flags.ToString());
                    WriteUUID(writer, "GroupID", item.GroupID, options);
                    writer.WriteElementString("GroupPermissions", item.GroupPermissions.ToString());
                    writer.WriteElementString("InvType", item.InvType.ToString());
                    WriteUUID(writer, "ItemID", item.ItemID, options);
                    WriteUUID(writer, "OldItemID", item.OldItemID, options);
                    WriteUUID(writer, "LastOwnerID", item.LastOwnerID, options);
                    writer.WriteElementString("Name", item.Name);
                    writer.WriteElementString("NextPermissions", item.NextPermissions.ToString());
                    WriteUUID(writer, "OwnerID", item.OwnerID, options);
                    writer.WriteElementString("CurrentPermissions", item.CurrentPermissions.ToString());
                    WriteUUID(writer, "ParentID", item.ParentID, options);
                    WriteUUID(writer, "ParentPartID", item.ParentPartID, options);
                    WriteUUID(writer, "PermsGranter", item.PermsGranter, options);
                    writer.WriteElementString("PermsMask", item.PermsMask.ToString());
                    writer.WriteElementString("Type", item.Type.ToString());
                    writer.WriteElementString("OwnerChanged", item.OwnerChanged.ToString().ToLower());

                    writer.WriteEndElement(); // TaskInventoryItem
                }

                writer.WriteEndElement(); // TaskInventory
            }
        }

        private static void WriteShape(XmlTextWriter writer, PrimitiveBaseShape shp, Dictionary<string, object> options)
        {
            if (shp != null)
            {
                writer.WriteStartElement("Shape");

                writer.WriteElementString("ProfileCurve", shp.ProfileCurve.ToString());

                writer.WriteStartElement("TextureEntry");
                byte[] te;
                if (shp.TextureEntry != null)
                    te = shp.TextureEntry;
                else
                    te = Utils.EmptyBytes;
                writer.WriteBase64(te, 0, te.Length);
                writer.WriteEndElement(); // TextureEntry

                writer.WriteStartElement("ExtraParams");
                byte[] ep;
                if (shp.ExtraParams != null)
                    ep = shp.ExtraParams;
                else
                    ep = Utils.EmptyBytes;
                writer.WriteBase64(ep, 0, ep.Length);
                writer.WriteEndElement(); // ExtraParams

                writer.WriteElementString("PathBegin", shp.PathBegin.ToString());
                writer.WriteElementString("PathCurve", shp.PathCurve.ToString());
                writer.WriteElementString("PathEnd", shp.PathEnd.ToString());
                writer.WriteElementString("PathRadiusOffset", shp.PathRadiusOffset.ToString());
                writer.WriteElementString("PathRevolutions", shp.PathRevolutions.ToString());
                writer.WriteElementString("PathScaleX", shp.PathScaleX.ToString());
                writer.WriteElementString("PathScaleY", shp.PathScaleY.ToString());
                writer.WriteElementString("PathShearX", shp.PathShearX.ToString());
                writer.WriteElementString("PathShearY", shp.PathShearY.ToString());
                writer.WriteElementString("PathSkew", shp.PathSkew.ToString());
                writer.WriteElementString("PathTaperX", shp.PathTaperX.ToString());
                writer.WriteElementString("PathTaperY", shp.PathTaperY.ToString());
                writer.WriteElementString("PathTwist", shp.PathTwist.ToString());
                writer.WriteElementString("PathTwistBegin", shp.PathTwistBegin.ToString());
                writer.WriteElementString("PCode", shp.PCode.ToString());
                writer.WriteElementString("ProfileBegin", shp.ProfileBegin.ToString());
                writer.WriteElementString("ProfileEnd", shp.ProfileEnd.ToString());
                writer.WriteElementString("ProfileHollow", shp.ProfileHollow.ToString());
                writer.WriteElementString("State", shp.State.ToString());

                writer.WriteElementString("ProfileShape", shp.ProfileShape.ToString());
                writer.WriteElementString("HollowShape", shp.HollowShape.ToString());

                WriteUUID(writer, "SculptTexture", shp.SculptTexture, options);
                writer.WriteElementString("SculptType", shp.SculptType.ToString());
                writer.WriteStartElement("SculptData");
                byte[] sd;
                //if (shp.SculptData != null)
                sd = shp.ExtraParams;
                //else
                //    sd = Utils.EmptyBytes;
                if (sd != null) writer.WriteBase64(sd, 0, sd.Length);
                writer.WriteEndElement(); // SculptData

                writer.WriteElementString("FlexiSoftness", shp.FlexiSoftness.ToString());
                writer.WriteElementString("FlexiTension", shp.FlexiTension.ToString());
                writer.WriteElementString("FlexiDrag", shp.FlexiDrag.ToString());
                writer.WriteElementString("FlexiGravity", shp.FlexiGravity.ToString());
                writer.WriteElementString("FlexiWind", shp.FlexiWind.ToString());
                writer.WriteElementString("FlexiForceX", shp.FlexiForceX.ToString());
                writer.WriteElementString("FlexiForceY", shp.FlexiForceY.ToString());
                writer.WriteElementString("FlexiForceZ", shp.FlexiForceZ.ToString());

                writer.WriteElementString("LightColorR", shp.LightColorR.ToString());
                writer.WriteElementString("LightColorG", shp.LightColorG.ToString());
                writer.WriteElementString("LightColorB", shp.LightColorB.ToString());
                writer.WriteElementString("LightColorA", shp.LightColorA.ToString());
                writer.WriteElementString("LightRadius", shp.LightRadius.ToString());
                writer.WriteElementString("LightCutoff", shp.LightCutoff.ToString());
                writer.WriteElementString("LightFalloff", shp.LightFalloff.ToString());
                writer.WriteElementString("LightIntensity", shp.LightIntensity.ToString());

                writer.WriteElementString("FlexiEntry", shp.FlexiEntry.ToString().ToLower());
                writer.WriteElementString("LightEntry", shp.LightEntry.ToString().ToLower());
                writer.WriteElementString("SculptEntry", shp.SculptEntry.ToString().ToLower());

                if (shp.Media != null)
                    writer.WriteElementString("Media", shp.Media.ToXml());

                writer.WriteEndElement(); // Shape
            }
        }

        //////// Read /////////
        public static bool Xml2ToSOG(XmlTextReader reader, SceneObjectGroup sog)
        {
            reader.Read();
            reader.ReadStartElement("SceneObjectGroup");
            SceneObjectPart root = Xml2ToSOP(reader, sog.Scene);
            if (root != null)
                sog.SetRootPart(root);
            else
            {
                return false;
            }

            if (sog.UUID == UUID.Zero)
                sog.UUID = sog.RootPart.UUID;

            reader.Read(); // OtherParts

            while (!reader.EOF)
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        if (reader.Name == "SceneObjectPart")
                        {
                            SceneObjectPart child = Xml2ToSOP(reader, sog.Scene);
                            if (child != null)
                                sog.AddChild(child, child.LinkNum);
                        }
                        else
                        {
                            //Logger.Log("Found unexpected prim XML element " + reader.Name, Helpers.LogLevel.Debug);
                            reader.Read();
                        }
                        break;
                    case XmlNodeType.EndElement:
                    default:
                        reader.Read();
                        break;
                }
            }
            return true;
        }

        public static SceneObjectPart Xml2ToSOP(XmlTextReader reader, IRegistryCore scene)
        {
            SceneObjectPart obj = new SceneObjectPart(scene);

            reader.ReadStartElement("SceneObjectPart");

            string nodeName = string.Empty;
            while (reader.Name != "SceneObjectPart")
            {
                nodeName = reader.Name;
                SOPXmlProcessor p = null;
                if (m_SOPXmlProcessors.TryGetValue(reader.Name, out p))
                {
                    //MainConsole.Instance.DebugFormat("[XXX] Processing: {0}", reader.Name);
                    try
                    {
                        p(obj, reader);
                    }
                    catch (Exception e)
                    {
                        MainConsole.Instance.DebugFormat("[SceneObjectSerializer]: exception while parsing {0}: {1}", nodeName, e);
                        if (reader.NodeType == XmlNodeType.EndElement)
                            reader.Read();
                    }
                }
                else
                {
                    //                    MainConsole.Instance.DebugFormat("[SceneObjectSerializer]: caught unknown element {0}", nodeName);
                    reader.ReadOuterXml(); // ignore
                }
            }

            reader.ReadEndElement(); // SceneObjectPart

            //MainConsole.Instance.DebugFormat("[XXX]: parsed SOP {0} - {1}", obj.Name, obj.UUID);
            return obj;
        }

        private static UUID ReadUUID(XmlTextReader reader, string name)
        {
            UUID id;
            string idStr;

            reader.ReadStartElement(name);

            idStr = reader.ReadElementString(reader.Name == "Guid" ? "Guid" : "UUID");

            UUID.TryParse(idStr, out id);
            reader.ReadEndElement();

            return id;
        }

        private static Vector3 ReadVector(XmlTextReader reader, string name)
        {
            Vector3 vec;

            reader.ReadStartElement(name);
            vec.X = reader.ReadElementContentAsFloat(reader.Name, String.Empty); // X or x
            vec.Y = reader.ReadElementContentAsFloat(reader.Name, String.Empty); // Y or y
            vec.Z = reader.ReadElementContentAsFloat(reader.Name, String.Empty); // Z or z
            reader.ReadEndElement();

            return vec;
        }

        private static Quaternion ReadQuaternion(XmlTextReader reader, string name)
        {
            Quaternion quat = new Quaternion();

            reader.ReadStartElement(name);
            while (reader.NodeType != XmlNodeType.EndElement)
            {
                switch (reader.Name.ToLower())
                {
                    case "x":
                        quat.X = reader.ReadElementContentAsFloat(reader.Name, String.Empty);
                        break;
                    case "y":
                        quat.Y = reader.ReadElementContentAsFloat(reader.Name, String.Empty);
                        break;
                    case "z":
                        quat.Z = reader.ReadElementContentAsFloat(reader.Name, String.Empty);
                        break;
                    case "w":
                        quat.W = reader.ReadElementContentAsFloat(reader.Name, String.Empty);
                        break;
                }
            }

            reader.ReadEndElement();

            return quat;
        }

        private static TaskInventoryDictionary ReadTaskInventory(XmlTextReader reader, string name)
        {
            TaskInventoryDictionary tinv = new TaskInventoryDictionary();

            reader.ReadStartElement(name, String.Empty);

            if (reader.IsEmptyElement)
            {
                reader.Read();
                return tinv;
            }

            while (reader.Name == "TaskInventoryItem")
            {
                reader.ReadStartElement("TaskInventoryItem", String.Empty); // TaskInventory

                TaskInventoryItem item = new TaskInventoryItem();
                while (reader.NodeType != XmlNodeType.EndElement)
                {
                    TaskInventoryXmlProcessor p = null;
                    try
                    {
                        if (m_TaskInventoryXmlProcessors.TryGetValue(reader.Name, out p))
                            p(item, reader);
                        else
                        {
                            //MainConsole.Instance.DebugFormat("[SceneObjectSerializer]: caught unknown element in TaskInventory {0}, {1}", reader.Name, reader.Value);
                            reader.ReadOuterXml();
                        }
                    }
                    catch (Exception e)
                    {
                        MainConsole.Instance.DebugFormat("[SceneObjectSerializer]: exception while parsing Inventory Items {0}: {1}",
                                          reader.Name, e);
                    }
                }
                reader.ReadEndElement(); // TaskInventoryItem
                tinv.Add(item.ItemID, item);
            }

            if (reader.NodeType == XmlNodeType.EndElement)
                reader.ReadEndElement(); // TaskInventory

            return tinv;
        }

        private static PrimitiveBaseShape ReadShape(XmlTextReader reader, string name)
        {
            PrimitiveBaseShape shape = new PrimitiveBaseShape();

            if (reader.IsEmptyElement)
            {
                reader.Read();
                return shape;
            }

            reader.ReadStartElement(name, String.Empty); // Shape

            string nodeName = string.Empty;
            while (reader.NodeType != XmlNodeType.EndElement)
            {
                nodeName = reader.Name;
                //MainConsole.Instance.DebugFormat("[XXX] Processing: {0}", reader.Name); 
                ShapeXmlProcessor p = null;
                if (m_ShapeXmlProcessors.TryGetValue(reader.Name, out p))
                {
                    try
                    {
                        p(shape, reader);
                    }
                    catch (Exception e)
                    {
                        MainConsole.Instance.DebugFormat("[SceneObjectSerializer]: exception while parsing Shape {0}: {1}", nodeName, e);
                        if (reader.NodeType == XmlNodeType.EndElement)
                            reader.Read();
                    }
                }
                else
                {
                    // MainConsole.Instance.DebugFormat("[SceneObjectSerializer]: caught unknown element in Shape {0}", reader.Name);
                    reader.ReadOuterXml();
                }
            }

            reader.ReadEndElement(); // Shape

            return shape;
        }

        #region SOPXmlProcessors

        private static void ProcessAllowedDrop(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.AllowedDrop = reader.ReadElementContentAsBoolean("AllowedDrop", String.Empty);
        }

        private static void ProcessCreatorID(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.CreatorID = ReadUUID(reader, "CreatorID");
        }

        private static void ProcessCreatorData(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.CreatorData = reader.ReadElementContentAsString("CreatorData", String.Empty);
        }

        private static void ProcessInventorySerial(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.InventorySerial = uint.Parse(reader.ReadElementContentAsString("InventorySerial", String.Empty));
        }

        private static void ProcessTaskInventory(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.TaskInventory = ReadTaskInventory(reader, "TaskInventory");
        }

        private static void ProcessUUID(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.UUID = ReadUUID(reader, "UUID");
        }

        private static void ProcessLocalId(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.LocalId = (uint) reader.ReadElementContentAsLong("LocalId", String.Empty);
        }

        private static void ProcessName(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Name = reader.ReadElementString("Name");
        }

        private static void ProcessMaterial(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Material = (byte) reader.ReadElementContentAsInt("Material", String.Empty);
        }

        private static void ProcessPassCollisions(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PassCollisions = reader.ReadElementContentAsInt("PassCollisions", String.Empty);
        }

        private static void ProcessPassTouch(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PassTouch = reader.ReadElementContentAsInt("PassTouch", String.Empty);
        }

        private static void ProcessScriptAccessPin(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.ScriptAccessPin = reader.ReadElementContentAsInt("ScriptAccessPin", String.Empty);
        }

        private static void ProcessGroupPosition(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.FixGroupPosition(ReadVector(reader, "GroupPosition"), false);
        }

        private static void ProcessOffsetPosition(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.FixOffsetPosition(ReadVector(reader, "OffsetPosition"), true);
        }

        private static void ProcessRotationOffset(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.RotationOffset = ReadQuaternion(reader, "RotationOffset");
        }

        private static void ProcessVelocity(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Velocity = ReadVector(reader, "Velocity");
        }

        private static void ProcessAngularVelocity(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.AngularVelocity = ReadVector(reader, "AngularVelocity");
        }

        private static void ProcessAcceleration(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Acceleration = ReadVector(reader, "Acceleration");
        }

        private static void ProcessDescription(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Description = reader.ReadElementString("Description");
        }

        private static void ProcessColor(SceneObjectPart obj, XmlTextReader reader)
        {
            reader.ReadStartElement("Color");
            if (reader.Name == "R")
            {
                float r = reader.ReadElementContentAsFloat("R", String.Empty);
                float g = reader.ReadElementContentAsFloat("G", String.Empty);
                float b = reader.ReadElementContentAsFloat("B", String.Empty);
                float a = reader.ReadElementContentAsFloat("A", String.Empty);
                obj.Color = Color.FromArgb((int) a, (int) r, (int) g, (int) b);
                reader.ReadEndElement();
            }
        }

        private static void ProcessText(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Text = reader.ReadElementString("Text", String.Empty);
        }

        private static void ProcessSitName(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.SitName = reader.ReadElementString("SitName", String.Empty);
        }

        private static void ProcessTouchName(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.TouchName = reader.ReadElementString("TouchName", String.Empty);
        }

        private static void ProcessLinkNum(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.LinkNum = reader.ReadElementContentAsInt("LinkNum", String.Empty);
        }

        private static void ProcessClickAction(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.ClickAction = (byte) reader.ReadElementContentAsInt("ClickAction", String.Empty);
        }

        private static void ProcessShape(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Shape = ReadShape(reader, "Shape");
        }

        private static void ProcessScale(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Scale = ReadVector(reader, "Scale");
        }

        private static void ProcessUpdateFlag(SceneObjectPart obj, XmlTextReader reader)
        {
            reader.Read();
            //InternalUpdateFlags flags = (InternalUpdateFlags)(byte)reader.ReadElementContentAsInt("UpdateFlag", String.Empty);
        }

        private static void ProcessSitTargetOrientation(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.SitTargetOrientation = ReadQuaternion(reader, "SitTargetOrientation");
        }

        private static void ProcessSitTargetPosition(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.SitTargetPosition = ReadVector(reader, "SitTargetPosition");
        }

        private static void ProcessSitTargetPositionLL(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.SitTargetPositionLL = ReadVector(reader, "SitTargetPositionLL");
        }

        private static void ProcessSitTargetOrientationLL(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.SitTargetOrientationLL = ReadQuaternion(reader, "SitTargetOrientationLL");
        }

        private static void ProcessParentID(SceneObjectPart obj, XmlTextReader reader)
        {
            string str = reader.ReadElementContentAsString("ParentID", String.Empty);
            obj.ParentID = Convert.ToUInt32(str);
        }

        private static void ProcessCreationDate(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.CreationDate = reader.ReadElementContentAsInt("CreationDate", String.Empty);
        }

        private static void ProcessCategory(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.Category = uint.Parse(reader.ReadElementContentAsString("Category", String.Empty));
        }

        private static void ProcessSalePrice(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.SalePrice = reader.ReadElementContentAsInt("SalePrice", String.Empty);
        }

        private static void ProcessObjectSaleType(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.ObjectSaleType = (byte) reader.ReadElementContentAsInt("ObjectSaleType", String.Empty);
        }

        private static void ProcessOwnershipCost(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.OwnershipCost = reader.ReadElementContentAsInt("OwnershipCost", String.Empty);
        }

        private static void ProcessGroupID(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.GroupID = ReadUUID(reader, "GroupID");
        }

        private static void ProcessOwnerID(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.OwnerID = ReadUUID(reader, "OwnerID");
        }

        private static void ProcessLastOwnerID(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.LastOwnerID = ReadUUID(reader, "LastOwnerID");
        }

        private static void ProcessBaseMask(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.BaseMask = uint.Parse(reader.ReadElementContentAsString("BaseMask", String.Empty));
        }

        private static void ProcessOwnerMask(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.OwnerMask = uint.Parse(reader.ReadElementContentAsString("OwnerMask", String.Empty));
        }

        private static void ProcessGroupMask(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.GroupMask = uint.Parse(reader.ReadElementContentAsString("GroupMask", String.Empty));
        }

        private static void ProcessEveryoneMask(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.EveryoneMask = uint.Parse(reader.ReadElementContentAsString("EveryoneMask", String.Empty));
        }

        private static void ProcessNextOwnerMask(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.NextOwnerMask = uint.Parse(reader.ReadElementContentAsString("NextOwnerMask", String.Empty));
        }

        private static void ProcessFlags(SceneObjectPart obj, XmlTextReader reader)
        {
            string value = reader.ReadElementContentAsString("Flags", String.Empty);
            // !!!!! to deal with flags without commas
            if (value.Contains(" ") && !value.Contains(","))
                value = value.Replace(" ", ", ");
            obj.Flags = (PrimFlags) Enum.Parse(typeof (PrimFlags), value);
        }

        private static void ProcessCollisionSound(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.CollisionSound = ReadUUID(reader, "CollisionSound");
        }

        private static void ProcessCollisionSoundVolume(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.CollisionSoundVolume =
                float.Parse(reader.ReadElementContentAsString("CollisionSoundVolume", String.Empty));
        }

        private static void ProcessMediaUrl(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.MediaUrl = reader.ReadElementContentAsString("MediaUrl", String.Empty);
        }

        private static void ProcessTextureAnimation(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.TextureAnimation =
                Convert.FromBase64String(reader.ReadElementContentAsString("TextureAnimation", String.Empty));
        }

        private static void ProcessParticleSystem(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.ParticleSystem =
                Convert.FromBase64String(reader.ReadElementContentAsString("ParticleSystem", String.Empty));
        }

        private static void ProcessPayPrice0(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PayPrice[0] = int.Parse(reader.ReadElementContentAsString("PayPrice0", String.Empty));
        }

        private static void ProcessPayPrice1(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PayPrice[1] = int.Parse(reader.ReadElementContentAsString("PayPrice1", String.Empty));
        }

        private static void ProcessPayPrice2(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PayPrice[2] = int.Parse(reader.ReadElementContentAsString("PayPrice2", String.Empty));
        }

        private static void ProcessPayPrice3(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PayPrice[3] = int.Parse(reader.ReadElementContentAsString("PayPrice3", String.Empty));
        }

        private static void ProcessPayPrice4(SceneObjectPart obj, XmlTextReader reader)
        {
            obj.PayPrice[4] = int.Parse(reader.ReadElementContentAsString("PayPrice4", String.Empty));
        }

        #endregion

        #region TaskInventoryXmlProcessors

        private static void ProcessTIAssetID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.AssetID = ReadUUID(reader, "AssetID");
        }

        private static void ProcessTIBasePermissions(TaskInventoryItem item, XmlTextReader reader)
        {
            item.BasePermissions = uint.Parse(reader.ReadElementContentAsString("BasePermissions", String.Empty));
        }

        private static void ProcessTICreationDate(TaskInventoryItem item, XmlTextReader reader)
        {
            item.CreationDate = uint.Parse(reader.ReadElementContentAsString("CreationDate", String.Empty));
        }

        private static void ProcessTICreatorID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.CreatorID = ReadUUID(reader, "CreatorID");
        }

        private static void ProcessTICreatorData(TaskInventoryItem item, XmlTextReader reader)
        {
            item.CreatorData = reader.ReadElementContentAsString("CreatorData", String.Empty);
        }

        private static void ProcessTIDescription(TaskInventoryItem item, XmlTextReader reader)
        {
            item.Description = reader.ReadElementContentAsString("Description", String.Empty);
        }

        private static void ProcessTIEveryonePermissions(TaskInventoryItem item, XmlTextReader reader)
        {
            item.EveryonePermissions = uint.Parse(reader.ReadElementContentAsString("EveryonePermissions", String.Empty));
        }

        private static void ProcessTIFlags(TaskInventoryItem item, XmlTextReader reader)
        {
            item.Flags = uint.Parse(reader.ReadElementContentAsString("Flags", String.Empty));
        }

        private static void ProcessTIGroupID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.GroupID = ReadUUID(reader, "GroupID");
        }

        private static void ProcessTIGroupPermissions(TaskInventoryItem item, XmlTextReader reader)
        {
            item.GroupPermissions = uint.Parse(reader.ReadElementContentAsString("GroupPermissions", String.Empty));
        }

        private static void ProcessTIInvType(TaskInventoryItem item, XmlTextReader reader)
        {
            item.InvType = reader.ReadElementContentAsInt("InvType", String.Empty);
        }

        private static void ProcessTIItemID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.ItemID = ReadUUID(reader, "ItemID");
        }

        private static void ProcessTIOldItemID(TaskInventoryItem item, XmlTextReader reader)
        {
            //Disable this, if we are rezzing from inventory, we want to get a new ItemID for next time
            //item.OldItemID = ReadUUID (reader, "OldItemID");
            ReadUUID(reader, "OldItemID");
        }

        private static void ProcessTILastOwnerID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.LastOwnerID = ReadUUID(reader, "LastOwnerID");
        }

        private static void ProcessTIName(TaskInventoryItem item, XmlTextReader reader)
        {
            item.Name = reader.ReadElementContentAsString("Name", String.Empty);
        }

        private static void ProcessTINextPermissions(TaskInventoryItem item, XmlTextReader reader)
        {
            item.NextPermissions = uint.Parse(reader.ReadElementContentAsString("NextPermissions", String.Empty));
        }

        private static void ProcessTIOwnerID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.OwnerID = ReadUUID(reader, "OwnerID");
        }

        private static void ProcessTICurrentPermissions(TaskInventoryItem item, XmlTextReader reader)
        {
            item.CurrentPermissions = uint.Parse(reader.ReadElementContentAsString("CurrentPermissions", String.Empty));
        }

        private static void ProcessTIParentID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.ParentID = ReadUUID(reader, "ParentID");
        }

        private static void ProcessTIParentPartID(TaskInventoryItem item, XmlTextReader reader)
        {
            item.ParentPartID = ReadUUID(reader, "ParentPartID");
        }

        private static void ProcessTIPermsGranter(TaskInventoryItem item, XmlTextReader reader)
        {
            item.PermsGranter = ReadUUID(reader, "PermsGranter");
        }

        private static void ProcessTIPermsMask(TaskInventoryItem item, XmlTextReader reader)
        {
            item.PermsMask = reader.ReadElementContentAsInt("PermsMask", String.Empty);
        }

        private static void ProcessTIType(TaskInventoryItem item, XmlTextReader reader)
        {
            item.Type = reader.ReadElementContentAsInt("Type", String.Empty);
        }

        private static void ProcessTIOwnerChanged(TaskInventoryItem item, XmlTextReader reader)
        {
            item.OwnerChanged = reader.ReadElementContentAsBoolean("OwnerChanged", String.Empty);
        }

        #endregion

        #region ShapeXmlProcessors

        private static void ProcessShpProfileCurve(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.ProfileCurve = (byte) reader.ReadElementContentAsInt("ProfileCurve", String.Empty);
        }

        private static void ProcessShpTextureEntry(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            byte[] teData = Convert.FromBase64String(reader.ReadElementString("TextureEntry"));
            shp.Textures = new Primitive.TextureEntry(teData, 0, teData.Length);
        }

        private static void ProcessShpExtraParams(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.ExtraParams = Convert.FromBase64String(reader.ReadElementString("ExtraParams"));
        }

        private static void ProcessShpPathBegin(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathBegin = (ushort) reader.ReadElementContentAsInt("PathBegin", String.Empty);
        }

        private static void ProcessShpPathCurve(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathCurve = (byte) reader.ReadElementContentAsInt("PathCurve", String.Empty);
        }

        private static void ProcessShpPathEnd(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathEnd = (ushort) reader.ReadElementContentAsInt("PathEnd", String.Empty);
        }

        private static void ProcessShpPathRadiusOffset(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathRadiusOffset = (sbyte) reader.ReadElementContentAsInt("PathRadiusOffset", String.Empty);
        }

        private static void ProcessShpPathRevolutions(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathRevolutions = (byte) reader.ReadElementContentAsInt("PathRevolutions", String.Empty);
        }

        private static void ProcessShpPathScaleX(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathScaleX = (byte) reader.ReadElementContentAsInt("PathScaleX", String.Empty);
        }

        private static void ProcessShpPathScaleY(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathScaleY = (byte) reader.ReadElementContentAsInt("PathScaleY", String.Empty);
        }

        private static void ProcessShpPathShearX(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathShearX = (byte) reader.ReadElementContentAsInt("PathShearX", String.Empty);
        }

        private static void ProcessShpPathShearY(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathShearY = (byte) reader.ReadElementContentAsInt("PathShearY", String.Empty);
        }

        private static void ProcessShpPathSkew(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathSkew = (sbyte) reader.ReadElementContentAsInt("PathSkew", String.Empty);
        }

        private static void ProcessShpPathTaperX(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathTaperX = (sbyte) reader.ReadElementContentAsInt("PathTaperX", String.Empty);
        }

        private static void ProcessShpPathTaperY(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathTaperY = (sbyte) reader.ReadElementContentAsInt("PathTaperY", String.Empty);
        }

        private static void ProcessShpPathTwist(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathTwist = (sbyte) reader.ReadElementContentAsInt("PathTwist", String.Empty);
        }

        private static void ProcessShpPathTwistBegin(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PathTwistBegin = (sbyte) reader.ReadElementContentAsInt("PathTwistBegin", String.Empty);
        }

        private static void ProcessShpPCode(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.PCode = (byte) reader.ReadElementContentAsInt("PCode", String.Empty);
        }

        private static void ProcessShpProfileBegin(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.ProfileBegin = (ushort) reader.ReadElementContentAsInt("ProfileBegin", String.Empty);
        }

        private static void ProcessShpProfileEnd(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.ProfileEnd = (ushort) reader.ReadElementContentAsInt("ProfileEnd", String.Empty);
        }

        private static void ProcessShpProfileHollow(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.ProfileHollow = (ushort) reader.ReadElementContentAsInt("ProfileHollow", String.Empty);
        }

        private static void ProcessShpScale(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.Scale = ReadVector(reader, "Scale");
        }

        private static void ProcessShpState(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.State = (byte) reader.ReadElementContentAsInt("State", String.Empty);
        }

        private static void ProcessShpProfileShape(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            string value = reader.ReadElementContentAsString("ProfileShape", String.Empty);
            // !!!!! to deal with flags without commas
            if (value.Contains(" ") && !value.Contains(","))
                value = value.Replace(" ", ", ");
            shp.ProfileShape = (ProfileShape) Enum.Parse(typeof (ProfileShape), value);
        }

        private static void ProcessShpHollowShape(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            string value = reader.ReadElementContentAsString("HollowShape", String.Empty);
            // !!!!! to deal with flags without commas
            if (value.Contains(" ") && !value.Contains(","))
                value = value.Replace(" ", ", ");
            shp.HollowShape = (HollowShape) Enum.Parse(typeof (HollowShape), value);
        }

        private static void ProcessShpSculptTexture(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.SculptTexture = ReadUUID(reader, "SculptTexture");
        }

        private static void ProcessShpSculptType(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.SculptType = (byte) reader.ReadElementContentAsInt("SculptType", String.Empty);
        }

        private static void ProcessShpSculptData(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.SculptData = Convert.FromBase64String(reader.ReadElementString("SculptData"));
        }

        private static void ProcessShpFlexiSoftness(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiSoftness = reader.ReadElementContentAsInt("FlexiSoftness", String.Empty);
        }

        private static void ProcessShpFlexiTension(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiTension = reader.ReadElementContentAsFloat("FlexiTension", String.Empty);
        }

        private static void ProcessShpFlexiDrag(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiDrag = reader.ReadElementContentAsFloat("FlexiDrag", String.Empty);
        }

        private static void ProcessShpFlexiGravity(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiGravity = reader.ReadElementContentAsFloat("FlexiGravity", String.Empty);
        }

        private static void ProcessShpFlexiWind(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiWind = reader.ReadElementContentAsFloat("FlexiWind", String.Empty);
        }

        private static void ProcessShpFlexiForceX(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiForceX = reader.ReadElementContentAsFloat("FlexiForceX", String.Empty);
        }

        private static void ProcessShpFlexiForceY(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiForceY = reader.ReadElementContentAsFloat("FlexiForceY", String.Empty);
        }

        private static void ProcessShpFlexiForceZ(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiForceZ = reader.ReadElementContentAsFloat("FlexiForceZ", String.Empty);
        }

        private static void ProcessShpLightColorR(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightColorR = reader.ReadElementContentAsFloat("LightColorR", String.Empty);
        }

        private static void ProcessShpLightColorG(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightColorG = reader.ReadElementContentAsFloat("LightColorG", String.Empty);
        }

        private static void ProcessShpLightColorB(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightColorB = reader.ReadElementContentAsFloat("LightColorB", String.Empty);
        }

        private static void ProcessShpLightColorA(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightColorA = reader.ReadElementContentAsFloat("LightColorA", String.Empty);
        }

        private static void ProcessShpLightRadius(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightRadius = reader.ReadElementContentAsFloat("LightRadius", String.Empty);
        }

        private static void ProcessShpLightCutoff(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightCutoff = reader.ReadElementContentAsFloat("LightCutoff", String.Empty);
        }

        private static void ProcessShpLightFalloff(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightFalloff = reader.ReadElementContentAsFloat("LightFalloff", String.Empty);
        }

        private static void ProcessShpLightIntensity(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightIntensity = reader.ReadElementContentAsFloat("LightIntensity", String.Empty);
        }

        private static void ProcessShpFlexiEntry(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.FlexiEntry = reader.ReadElementContentAsBoolean("FlexiEntry", String.Empty);
        }

        private static void ProcessShpLightEntry(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.LightEntry = reader.ReadElementContentAsBoolean("LightEntry", String.Empty);
        }

        private static void ProcessShpSculptEntry(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            shp.SculptEntry = reader.ReadElementContentAsBoolean("SculptEntry", String.Empty);
        }

        private static void ProcessShpMedia(PrimitiveBaseShape shp, XmlTextReader reader)
        {
            string value = reader.ReadElementContentAsString("Media", String.Empty);
            shp.Media = PrimitiveBaseShape.MediaList.FromXml(value);
        }

        #endregion

        #region Nested type: Serialization

        private delegate string Serialization(SceneObjectPart part);

        #endregion

        #region Nested type: ShapeXmlProcessor

        private delegate void ShapeXmlProcessor(PrimitiveBaseShape shape, XmlTextReader reader);

        #endregion

        #region Nested type: TaskInventoryXmlProcessor

        private delegate void TaskInventoryXmlProcessor(TaskInventoryItem item, XmlTextReader reader);

        #endregion

        #endregion
    }
}
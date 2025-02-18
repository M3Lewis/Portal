using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Portal.Gh.Common;
using Portal.Gh.Params.Payloads;
using Portal.Gh.Params.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Portal.Core.DataModel;
using Portal.Gh.Components.Serialization.JsonSerializerSettings;
using Rhino.DocObjects;

namespace Portal.Gh.Components.Serialization
{
    public class DeserializeGeometryComponent : GH_Component
    {
        public DeserializeGeometryComponent()
            : base("Deserialize Geometry", "DSrGeo",
                "Deserialize a JSON Packet back into Rhino Geometry",
                Config.Category, Config.SubCat.Serialization)
        {
        }

        #region Metadata

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override IEnumerable<string> Keywords => new string[] { };
        protected override Bitmap Icon => Icons.DeserializeGeo;
        public override Guid ComponentGuid => new Guid("492a36d8-5074-4ec0-b632-cb1dc51dad45");

        #endregion

        #region IO

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(new PayloadParam(), "Payload", "P", "Payload packet to be deserialize", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Geometry", "M", "Deserialized mesh", GH_ParamAccess.item);
            pManager.AddParameter(new JsonDictParam(), "Metadata", "#", "Metadata that describe the geometry",
                GH_ParamAccess.item);
        }

        #endregion

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            PayloadGoo payloadGoo = new PayloadGoo();

            if (!DA.GetData(0, ref payloadGoo)) return;

            PGeoType geoType = TryGetType(payloadGoo.Value);

            switch (geoType)
            {
                case PGeoType.Mesh:
                    var mesh = DeserializeMesh(payloadGoo.Value.Items.ToString());
                    var meta = new JsonDictGoo(payloadGoo.Value.Meta);
                    DA.SetData(0, mesh);
                    DA.SetData(1, meta);
                    break;
                case PGeoType.Curve:
                    var curve = DeserializeCurve(payloadGoo.Value.Items.ToString());
                    var metaCurve = new JsonDictGoo(payloadGoo.Value.Meta);
                    DA.SetData(0, curve);
                    DA.SetData(1, metaCurve);
                    break;
                default:
                    try
                    {
                        var point = DeserializePoint(payloadGoo.Value.Items.ToString());
                        var metaPoint = new JsonDictGoo(payloadGoo.Value.Meta);
                        DA.SetData(0, point);
                        DA.SetData(1, metaPoint);
                    }
                    catch (Exception e)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to deserialize geometry: {e.Message}");
                    }
                    break;
            }
        }

        private PGeoType TryGetType(Payload payload)
        {
            if (payload == null || payload.Items == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Payload is null or empty.");
                return PGeoType.Undefined; // Ensure payload is not null
            }

            string payloadJson = JsonConvert.SerializeObject(payload.Items);
            JObject parsedObject = JObject.Parse(payloadJson);

            if (parsedObject["Type"] == null)
            {
                //AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Type property is missing from the payload item.");
                return PGeoType.Undefined; // Early exit if no 'Type' could be identified
            }

            PGeoType itemType;
            try
            {
                itemType = parsedObject["Type"].ToObject<PGeoType>();
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to parse item type: {ex.Message}");
                return PGeoType.Undefined;
            }

            return itemType;
        }

        private Mesh DeserializeMesh(string data)
        {
            PMesh dataMesh = JsonConvert.DeserializeObject<PMesh>(data);
            if (dataMesh == null)
            {
                throw new InvalidOperationException("No meshes found in the JSON data.");
            }

            Mesh mesh = new Mesh();
            mesh.Vertices.AddVertices(dataMesh.Vertices.Select(vertex => new Point3d(vertex.X, vertex.Y, vertex.Z)));
            mesh.Faces.AddFaces(dataMesh.Faces.Select(face => new MeshFace(face[0], face[1], face[2], face[3])).ToArray());
            foreach (var hexColor in dataMesh.VertexColors)
            {
                PColor pColor = PColor.FromHexColor(hexColor);
                mesh.VertexColors.Add(pColor.R, pColor.G, pColor.B);
            }

            if (dataMesh.UVs != null && dataMesh.UVs.Count > 0)
            {
                mesh.TextureCoordinates.AddRange(dataMesh.UVs.Select(uv => new Point2f(uv.X, uv.Y)).ToArray());
            }
            mesh.Normals.ComputeNormals();

            return mesh;
        }

        private Point3d? DeserializePoint(string data)
        {
            try
            {
                dynamic pPoint = JsonConvert.DeserializeObject<dynamic>(data);

                if (pPoint == null)
                {
                    throw new JsonSerializationException("Data is null. Check JSON.");
                }

                if (!PVector3D.Validate(pPoint))
                {
                    throw new JsonSerializationException("Deserialization resulted in invalid data. Check the JSON structure.");
                }

                Point3d point = new Point3d(pPoint.X, pPoint.Y, pPoint.Z);

                return point;
            }
            catch (JsonSerializationException e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Error deserializing JSON data: {e.Message}");
            }
            catch (Exception e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"An error occurred: {e.Message}");
            }

            // return empty point if deserialization fails
            return null;
        }

        private Curve DeserializeCurve(string jsonData)
        {
            var serializerSettings = new Newtonsoft.Json.JsonSerializerSettings();
            serializerSettings.Converters.Add(new PCurveConverterSettings());

            PCurve pCurve = JsonConvert.DeserializeObject<PCurve>(jsonData, serializerSettings);

            switch (pCurve)
            {
                case PNurbsCurve nc:
                    return NurbsCurve.Create(
                        nc is { IsPeriodic: true },
                        nc.Degree,
                        nc.Points.Select(point => new Point3d(point.X, point.Y, point.Z))
                    );
                case PLine lc:
                    return new LineCurve(
                        new Point3d(lc.Points[0].X, lc.Points[0].Y, lc.Points[0].Z),
                        new Point3d(lc.Points[1].X, lc.Points[1].Y, lc.Points[1].Z)
                    );
                case PPolylineCurve pc:
                    return new PolylineCurve(pc.Points.Select(point => new Point3d(point.X, point.Y, point.Z)));
                case PArcCurve arc:
                    return ConstructCurve(arc);
                default:
                    throw new NotImplementedException($"Deserialization of {pCurve.Type} is not implemented");
            }
        }

        private Light DeserializeLight(string jsonData)
        {
            var serializerSettings = new Newtonsoft.Json.JsonSerializerSettings();
            serializerSettings.Converters.Add(new PLightConverterSettings());

            PLight pLight = JsonConvert.DeserializeObject<PLight>(jsonData, serializerSettings);

            switch (pLight)
            {
                case PPointLight pointLight:
                    return new Light()
                    {
                        Diffuse = pointLight.LightDiffuseColor.ToARGB(),
                        AttenuationType = pointLight.LightAttenuationType,
                        Location = new Point3d(pointLight.LightLocation.X, pointLight.LightLocation.Y, pointLight.LightLocation.Z),
                        Intensity = pointLight.LightIntensity,
                    };
                
                case PRectangularLight rectLight:
                    return new Light()
                    {
                        Diffuse = rectLight.LightDiffuseColor.ToARGB(),
                        AttenuationType = rectLight.LightAttenuationType,
                        Location = new Point3d(rectLight.LightLocation.X, rectLight.LightLocation.Y, rectLight.LightLocation.Z),
                        Direction = new Vector3d(rectLight.LightDirection.X, rectLight.LightDirection.Y, rectLight.LightDirection.Z),
                        Intensity = rectLight.LightIntensity,
                        Length = new Vector3d(rectLight.LightLength.X, rectLight.LightLength.Y, rectLight.LightLength.Z),
                        Width = new Vector3d(rectLight.LightWidth.X, rectLight.LightWidth.Y, rectLight.LightWidth.Z),
                    };

                case PSpotLight spotLight:
                    return new Light()
                    {
                        Diffuse = spotLight.LightDiffuseColor.ToARGB(),
                        AttenuationType = spotLight.LightAttenuationType,
                        Location = new Point3d(spotLight.LightLocation.X, spotLight.LightLocation.Y, spotLight.LightLocation.Z),
                        Direction = new Vector3d(spotLight.LightDirection.X, spotLight.LightDirection.Y, spotLight.LightDirection.Z),
                        Intensity = spotLight.LightIntensity,
                        SpotAngleRadians = spotLight.LightSpotAngleRadians,
                        HotSpot = spotLight.LightHotSpot,
                        ShadowIntensity = spotLight.LightShadowIntensity
                    };

                case PSunLight sunLight:
                    return new Light()
                    {
                        Diffuse = sunLight.LightDiffuseColor.ToARGB(),
                        AttenuationType = sunLight.LightAttenuationType,
                        Location = new Point3d(sunLight.LightLocation.X, sunLight.LightLocation.Y, sunLight.LightLocation.Z),
                        Intensity = sunLight.LightIntensity,
                    };

                default:
                    throw new NotImplementedException($"Deserialization of {pLight.Type} is not implemented");
            }
        }


        private ArcCurve ConstructCurve(PArcCurve curve)
        {
            Plane plane = new Plane(
                new Point3d(curve.Plane.Origin.X, curve.Plane.Origin.Y, curve.Plane.Origin.Z),
                new Vector3d(curve.Plane.XAxis.X, curve.Plane.XAxis.Y, curve.Plane.XAxis.Z),
                new Vector3d(curve.Plane.YAxis.X, curve.Plane.YAxis.Y, curve.Plane.YAxis.Z)
            );
            return new ArcCurve(new Arc(plane, curve.Radius, curve.AngleRadiant));
        }
    }
}
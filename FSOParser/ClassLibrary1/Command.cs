using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.ApplicationServices;
using System.Net.Http;
using Autodesk.Revit.DB.Analysis;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using System.IO;

/***************************************
 * Code structure:
 * 1. Parse the project information and specify the file path for serialization..
 * 2. Parse the building, level, and the zone related information.
 * 3. Relationship between ventilation systems and their components. (Exhaust, supply and return air)
 * 4. Relationship between heating and cooling systems and their components.
 * 5. Get IfcExportAs components, pipe fittings, duct fittings; Instatiate the components' properties and the connections between them
 * 6. Get Pump instances (Properties and connections)
 * 7. Get Duct instances (Properties and connections)
 * 8. Instatiate the turtle file or send it to the database
 * 
 * To do:
 *  
****************************************/


namespace ClassLibrary1
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            //*************
            //Get the building and assign it as buildingname. Working
            ProjectInfo projectinfo = doc.ProjectInformation;
            string buildingname = projectinfo.BuildingName;
            string buildingGuid = projectinfo.UniqueId.ToString();
            string docPathName = doc.PathName;
            string fpath = docPathName.Replace(".rvt", ".ttl");
            string docName = doc.Title;

            //************
            // Genrate the header
            StringBuilder sb = new StringBuilder();
            sb.Append(
                $"# baseURI: file:/{fpath} \n" +
                "# imports: https://w3id.org/bot# \n" +
                "# imports: http://bosch-cr-aes//hsbc/fsoension\r\n" +
                "# imports: http://bosch-cr-aes//hsbc/fpoension\r\n" +
                "# imports: https://brickschema.org/schema/1.3/Brick\n\n" +
                "@prefix owl: <http://www.w3.org/2002/07/owl#> ." + "\n" +
                "@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> ." + "\n" +
                "@prefix xml: <http://www.w3.org/XML/1998/namespace> ." + "\n" +
                "@prefix xsd: <http://www.w3.org/2001/XMLSchema#> ." + "\n" +
                "@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> ." + "\n" +
                "@prefix bot: <https://w3id.org/bot#> ." + "\n" +
                "@prefix brick: <https://brickschema.org/schema/Brick#> ." + "\n" +
                "@prefix props: <https://w3id.org/props#> ." + "\n" +
                "@prefix fso: <https://w3id.org/fso#> ." + "\n" +
                "@prefix fpo: <https://w3id.org/fpo#> ." + "\n" +
                "@prefix ssn: <http://www.w3.org/ns/ssn/> ." + "\n" +
                "@prefix sosa: <http://www.w3.org/ns/sosa/> ." + "\n" +
                "@prefix unit: <http://qudt.org/vocab/unit/> ." + "\n" +
                "@prefix inst: <https://example.com/inst#> ." + "\n\n" 
                );

            //Generate the building
            sb.Append($"inst:Building_{buildingGuid} a bot:Building ;" + "\n" +
                $"\t props:hasGuid '{buildingGuid}'^^xsd:string  ;" + "\n"+
                $"\t rdfs:label '{buildingname}'^^xsd:string  ." + "\n\n");


            //Get all level and the building it is related to. WOKRING 
            FilteredElementCollector levelCollector = new FilteredElementCollector(doc);
            ICollection<Element> levels = levelCollector.OfClass(typeof(Level)).ToElements();
            List<Level> levelList = new List<Level>();
            foreach (Level level in levelCollector)
            {
                Level w = level as Level;
                string levelName = level.Name.Replace(' ', '-');
                string guidNumber = level.UniqueId.ToString();
                sb.Append($"inst:Storey_{guidNumber} a bot:Storey ;" + "\n" +
                    $"\t props:hasGuid '{guidNumber}'^^xsd:string  ;" + "\n" +
                    $"\t rdfs:label '{levelName}'^^xsd:string ." + "\n" + 
                    $"inst:Building_{buildingGuid} bot:hasStorey inst:Storey_{guidNumber} ." + "\n\n");
            }

            // Get all spaces and the attributes related to. WOKRING
            FilteredElementCollector roomCollector = new FilteredElementCollector(doc);
            ICollection<Element> rooms = roomCollector.OfClass(typeof(SpatialElement)).ToElements();
            List<SpatialElement> roomList = new List<SpatialElement>();
            foreach (SpatialElement space in roomCollector)
            {
                SpatialElement w = space as SpatialElement;
                if (space.Category.Name == "Spaces" & space.LookupParameter("Area").AsDouble() > 0)
                {
                    string spaceName = space.Name.Replace(' ', '_');
                    string spaceGuid = space.UniqueId.ToString();
                    string isSpaceOf = space.Level.UniqueId;
                    double area = space.LookupParameter("Area").AsDouble();

                    string designCoolingLoadID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double designCoolingLoad = UnitUtils.ConvertFromInternalUnits(space.LookupParameter("Design Cooling Load").AsDouble(), UnitTypeId.Watts);

                    string designHeatingLoadID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double designHeatingLoad = UnitUtils.ConvertFromInternalUnits(space.LookupParameter("Design Heating Load").AsDouble(), UnitTypeId.Watts);

                    string designSupplyAirflowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double designSupplyAirflow = UnitUtils.ConvertFromInternalUnits(space.LookupParameter("Actual Supply Airflow").AsDouble(), UnitTypeId.LitersPerSecond);

                    string designReturnAirflowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double designReturnAirflow = UnitUtils.ConvertFromInternalUnits(space.LookupParameter("Actual Return Airflow").AsDouble(), UnitTypeId.LitersPerSecond);

                sb.Append($"inst:Space_{spaceGuid} a bot:Space ;" + "\n" +
                        $"\tssn:hasProperty inst:CoolingLoad_{designCoolingLoadID},inst:HeatingLoad_{designHeatingLoadID}, inst:Airflow_{designSupplyAirflowID}, inst:Airflow_{designReturnAirflowID} ;" + "\n" +
                        $"\tprops:hasGuid '{spaceGuid}'^^xsd:string  ;" + "\n" +
                        $"\tprops:hasArea '{area}'^^xsd:double  ;\r\n" +
                        $"\trdfs:label '{spaceName}'^^xsd:string ." + "\n" +
                        $"inst:Storey_{isSpaceOf} bot:hasSpace inst:Space_{spaceGuid} ." + "\n" +

                        $"#Cooling Demand in Space_{spaceName}" + "\n" +
                        $"inst:CoolingLoad_{designCoolingLoadID} a ssn:DesignCoolingDemand ;" + "\n" +
                        $"\tbrick:value '{designCoolingLoad}'^^xsd:double ;" + "\n" +
                        $"\tbrick:hasUnit unit:W ." + "\n" +

                        $"#Heating Demand in Space_{spaceName}" + "\n" +
                        $"inst:HeatingLoad_{designHeatingLoadID} a ssn:DesignHeatingDemand ;" + "\n" +
                        $"\tbrick:value '{designHeatingLoad}'^^xsd:double ;" + "\n" +
                        $"\tbrick:hasUnit unit:W ." + "\n" +

                        $"#Supply Air Flow Demand in Space_{spaceName}" + "\n" +
                        $"inst:Airflow_{designSupplyAirflowID} a ssn:DesignSupplyAirflowDemand ;" + "\n" +
                        $"\tbrick:value '{designSupplyAirflow}'^^xsd:double ;" + "\n" +
                        $"\tbrick:hasUnit unit:L-PER-SEC ." + "\n" +

                        $"#Return Air Flow Demand in Space_{spaceName}" + "\n" +
                        $"inst:Airflow_{designReturnAirflowID} a ssn:DesignReturnAirflowDemand ;" + "\n"+
                        $"\tbrick:value '{designReturnAirflow}'^^xsd:double ;" + "\n" +
                        $"\tbrick:hasUnit unit:L-PER-SEC ." + "\n\n");
                };
            }

            //Relationship between ventilation systems and their components.
            FilteredElementCollector ventilationSystemCollector = new FilteredElementCollector(doc);
            ICollection<Element> ventilationSystems = ventilationSystemCollector.OfClass(typeof(MechanicalSystem)).ToElements();
            List<MechanicalSystem> ventilationSystemList = new List<MechanicalSystem>();
            foreach (MechanicalSystem system in ventilationSystemCollector)
            {
                //Get systems
                DuctSystemType systemType = system.SystemType;

                string systemID = system.UniqueId;
                string systemName = system.Name;
                ElementId superSystemType = system.GetTypeId();
                string fluidID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                string flowTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-');

                string fluidTemperatureID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                //double fluidTemperature = UnitUtils.ConvertFromInternalUnits(system.LookupParameter("Fluid Temperature").AsDouble(), UnitTypeId.Celsius);

                switch (systemType)
                {
                    case DuctSystemType.SupplyAir:
                        sb.Append($"inst:VentilationSys_{systemID} a fso:SupplySystem ;" + "\n" +
                            $"\t rdfs:label '{systemName}'^^xsd:string ;" + "\n" +
                            $"\t brick:hasSubstance inst:Substance_{fluidID} ." + "\n" +
                            $"inst:Substance_{fluidID} a brick:Supply_Air ;" + "\n" +
                            //$"inst:{fluidID} fpo:hasFlowType inst:{flowTypeID} ." + "\n" +
                            //$"inst:{flowTypeID} a fpo:FlowType ." + "\n" +
                            $"\t rdfs:label 'Air'^^xsd:string .\n\n" 

                            //$"inst:Substance_{fluidID} ssn:hasProperty inst:Temperature_{fluidTemperatureID} ." + "\n" +
                            //$"inst:Temperature_{fluidTemperatureID} a fpo:NominalTemperature ." + "\n" +
                            //$"inst:Temperature_{fluidTemperatureID} brick:value '{fluidTemperature}'^^xsd:double ." + "\n" +
                            //$"inst:Temperature_{fluidTemperatureID} brick:hasUnit unit:DEG_C ." + "\n"
                            );
                        break;
                    case DuctSystemType.ReturnAir:
                        sb.Append($"inst:VentilationSys_{systemID} a fso:ReturnSystem ;" + "\n"
                                + $"\trdfs:label '{systemName}'^^xsd:string ;" + "\n" +
                            $"\tbrick:hasSubstance inst:Substance_{fluidID} ." + "\n" +
                            $"inst:Substance_{fluidID} a brick:Return_Air ;" + "\n" +
                            $"\trdfs:label 'Air'^^xsd:string .\n\n"

                            //$"inst:Substance_{fluidID} ssn:hasProperty inst:Temperature_{fluidTemperatureID} ." + "\n" +
                            //$"inst:Temperature_{fluidTemperatureID} a fpo:NominalTemperature ." + "\n" +
                            //$"inst:Temperature_{fluidTemperatureID} brick:value '{fluidTemperature}'^^xsd:double ." + "\n" +
                            //$"inst:Temperature_{fluidTemperatureID} brick:hasUnit unit:DEG_C ." + "\n"
                            );
                        break;
                    case DuctSystemType.ExhaustAir:
                        sb.Append($"inst:VentilationSys_{systemID} a fso:ReturnSystem ;" + "\n"
                                + $"\trdfs:label '{systemName}'^^xsd:string ;" + "\n" +
                                $"\tbrick:hasSubstance inst:Substance_{fluidID} ." + "\n" +

                            $"inst:Substance_{fluidID} a brick:Exhaust_Air ;" + "\n" +
                            $"\trdfs:label 'Air'^^xsd:string .\n\n"
                            // + "\n"+
                            //$"inst:Substance_{fluidID} ssn:hasProperty inst:Temperature_{fluidTemperatureID} ." + "\n" +
                            //$"inst:Temperature_{fluidTemperatureID} a fpo:NominalTemperature ." + "\n" +
                            //$"inst:Temperature_{fluidTemperatureID} brick:value '{fluidTemperature}'^^xsd:double ." + "\n" +
                            //$"inst:Temperature_{fluidTemperatureID} brick:hasUnit unit:DEG_C ." + "\n"
                            );
                        break;
                    default:
                        break;
                }

                ElementSet systemComponents = system.DuctNetwork;

                //Relate components to systems
                foreach (Element component in systemComponents)
                {
                    string componentID = component.UniqueId;
                    sb.Append($"inst:VentilationSys_{systemID} fso:hasComponent inst:Comp_{componentID} ." + "\n");
                }
            }


            // *****************
            // Relationship between heating and cooling systems and their components. Working
            FilteredElementCollector hydraulicSystemCollector = new FilteredElementCollector(doc) ;
            ICollection<Element> hydraulicSystems = hydraulicSystemCollector.OfClass(typeof(PipingSystem)).ToElements();
            List<PipingSystem> hydraulicSystemList = new List<PipingSystem>();
            foreach (PipingSystem system in hydraulicSystemCollector)
            {
                //Get systems
                PipeSystemType systemType = system.SystemType;
                string systemID = system.UniqueId;
                string systemName = system.Name;
                ElementId superSystemType = system.GetTypeId();

                //Fluid
                string fluidID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                //string flowTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                string fluidTemperatureID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                //string fluidViscosityID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                //string fluidDensityID = System.Guid.NewGuid().ToString().Replace(' ', '-');

                if ( doc.GetElement(superSystemType).LookupParameter("Fluid Type") != null )
                {
                    string flowType = doc.GetElement(superSystemType).LookupParameter("Fluid Type").AsValueString();
                    //string fluidTemperature = doc.GetElement(superSystemType).LookupParameter("Fluid Temperature").AsValueString(); //  Why make the user defined-property "Fluid TemperatureX"?
                    double fluidTemperature = UnitUtils.ConvertFromInternalUnits(doc.GetElement(superSystemType).LookupParameter("Fluid Temperature").AsDouble(), UnitTypeId.Celsius); //  Why make the user defined-property "Fluid TemperatureX"?
                    //double fluidViscosity = UnitUtils.ConvertFromInternalUnits(doc.GetElement(superSystemType).LookupParameter("Fluid Dynamic Viscosity").AsDouble(), UnitTypeId.PascalSeconds);
                    //double fluidDensity = UnitUtils.ConvertFromInternalUnits(doc.GetElement(superSystemType).LookupParameter("Fluid Density").AsDouble(), UnitTypeId.KilogramsPerCubicMeter);

                    switch (systemType)
                    {
                        case PipeSystemType.SupplyHydronic:
                            sb.Append(
                                // *******************Original Start******************************************************* 
                                //$"inst:{systemID} a fso:SupplySystem ." + "\n"
                                //+ $"inst:{systemID} rdfs:label '{systemName}'^^xsd:string ." + "\n" +
                                //$"inst:{systemID} fpo:hasFlow inst:{fluidID} ." + "\n" +

                                //$"inst:{fluidID} a fpo:Flow ." + "\n" +
                                //$"inst:{fluidID} fpo:hasFlowType inst:{flowTypeID} ." + "\n" +
                                //$"inst:{flowTypeID} a fpo:FlowType ." + "\n" +
                                //$"inst:{flowTypeID} fpo:hasValue '{flowType}'^^xsd:string ." + "\n" +

                                //$"inst:{fluidID} fpo:hasTemperature inst:{fluidTemperatureID} ." + "\n" +
                                //$"inst:{fluidTemperatureID} a fpo:Temperature ." + "\n" +
                                //$"inst:{fluidTemperatureID} fpo:hasValue '{fluidTemperature}'^^xsd:double ." + "\n" +
                                //$"inst:{fluidTemperatureID} fpo:hasUnit 'Celcius'^^xsd:string ." + "\n"

                                //$"inst:{fluidID} fpo:hasViscosity inst:{fluidViscosityID} ." + "\n" +
                                //$"inst:{fluidViscosityID} a fpo:Viscosity ." + "\n" +
                                //$"inst:{fluidViscosityID} fpo:hasValue '{fluidViscosity}'^^xsd:double ." + "\n" +
                                //$"inst:{fluidViscosityID} fpo:hasUnit 'Pascal per second'^^xsd:string ." + "\n" +

                                //$"inst:{fluidID} fpo:hasDensity inst:{fluidDensityID} ." + "\n" +
                                //$"inst:{fluidDensityID} a fpo:Density ." + "\n" +
                                //$"inst:{fluidDensityID} fpo:hasValue '{fluidDensity}'^^xsd:double ." + "\n" +
                                //$"inst:{fluidDensityID} fpo:hasUnit 'Kilograms per cubic meter'^^xsd:string ." + "\n"
                                // *******************Original End******************************************************* 

                                $"inst:HydraulicSys_{systemID} a fso:SupplySystem ;" + "\n" +
                                $"\trdfs:label '{systemName}'^^xsd:string ;" + "\n" +
                                $"\tbrick:hasSubstance inst:Substance_{fluidID} ." + "\n" +

                                $"inst:Substanace_{fluidID} a brick:Supply_Water ;" + "\n" +
                                $"\trdfs:label '{flowType}'^^xsd:string ;" + "\n" +
                                $"\tssn:hasProperty inst:Temperature_{fluidTemperatureID} ." + "\n" +

                                $"inst:Temperature_{fluidTemperatureID} a fpo:NominalTemperature ;" + "\n" +
                                $"\tbrick:value '{fluidTemperature}'^^xsd:double ;" + "\n" +
                                $"\tbrick:hasUnit unit:DEG_C ." + "\n\n"

                                //$"inst:{fluidID} fpo:hasViscosity inst:{fluidViscosityID} ." + "\n" +
                                //$"inst:{fluidViscosityID} a fpo:Viscosity ." + "\n" +
                                //$"inst:{fluidViscosityID} fpo:hasValue '{fluidViscosity}'^^xsd:double ." + "\n" +
                                //$"inst:{fluidViscosityID} fpo:hasUnit 'Pascal per second'^^xsd:string ." + "\n" +

                                //$"inst:{fluidID} fpo:hasDensity inst:{fluidDensityID} ." + "\n" +
                                //$"inst:{fluidDensityID} a fpo:Density ." + "\n" +
                                //$"inst:{fluidDensityID} fpo:hasValue '{fluidDensity}'^^xsd:double ." + "\n" +
                                //$"inst:{fluidDensityID} fpo:hasUnit 'Kilograms per cubic meter'^^xsd:string ." + "\n"
                                );
                            break;
                        case PipeSystemType.ReturnHydronic:
                            sb.Append(
                                // *******************Original Start******************************************************* 
                                // $"inst:{systemID} a fso:ReturnSystem ." + "\n" +
                                // $"inst:{systemID} rdfs:label '{systemName}'^^xsd:string ." + "\n" +

                                // $"inst:{systemID} fso:hasFlow inst:{fluidID} ." + "\n" +

                                //$"inst:{fluidID} a fso:Flow ." + "\n" +
                                // $"inst:{fluidID} fpo:hasFlowType inst:{flowTypeID} ." + "\n" +
                                // $"inst:{flowTypeID} a fpo:FlowType ." + "\n" +
                                // $"inst:{flowTypeID} fpo:hasValue '{flowType}'^^xsd:string ." + "\n" +

                                // $"inst:{fluidID} fpo:hasTemperature inst:{fluidTemperatureID} ." + "\n" +
                                // $"inst:{fluidTemperatureID} a fpo:Temperature ." + "\n" +
                                // $"inst:{fluidTemperatureID} brick:value '{fluidTemperature}'^^xsd:double ." + "\n" +
                                // $"inst:{fluidTemperatureID} fpo:hasUnit 'Celcius'^^xsd:string ." + "\n"

                                //$"inst:{fluidID} fpo:hasViscosity inst:{fluidViscosityID} ." + "\n" +
                                //$"inst:{fluidViscosityID} a fpo:Viscosity ." + "\n" +
                                //$"inst:{fluidViscosityID} fpo:hasValue '{fluidViscosity}'^^xsd:double ." + "\n" +
                                //$"inst:{fluidViscosityID} fpo:hasUnit 'Pascal per second'^^xsd:string ." + "\n" +

                                //$"inst:{fluidID} fpo:hasDensity inst:{fluidDensityID} ." + "\n" +
                                //$"inst:{fluidDensityID} a fpo:Density ." + "\n" +
                                //$"inst:{fluidDensityID} fpo:hasValue '{fluidDensity}'^^xsd:double ." + "\n" +
                                //$"inst:{fluidDensityID} fpo:hasUnit 'Kilograms per cubic meter'^^xsd:string ." + "\n"
                                // *******************Original End******************************************************* 

                                $"inst:HydraulicSys_{systemID} a fso:ReturnSystem ;" + "\n" +
                                $"\trdfs:label '{systemName}'^^xsd:string ;" + "\n" +
                                $"\tbrick:hasSubstance inst:Substance_{fluidID} ." + "\n" +

                                $"inst:Substance_{fluidID} a brick:Return_Water ;" + "\n" +
                                $"\trdfs:label '{flowType}'^^xsd:string ;" + "\n" +
                                //$"inst:Substance_{fluidID} fpo:hasFlowType inst:FlowType_{flowTypeID} ." + "\n" +
                                //$"inst:FlowType_{flowTypeID} a fpo:FlowType ." + "\n" +
                                //$"inst:FlowType_{flowTypeID} brick:value '{flowType}'^^xsd:string ." + "\n" +
                                $"\tssn:hasProperty inst:Temperature_{fluidTemperatureID} ." + "\n" +

                                $"inst:Temperature_{fluidTemperatureID} a fpo:NominalTemperature ;" + "\n" +
                                $"\tbrick:value '{fluidTemperature}'^^xsd:double ;" + "\n" +
                                $"\tbrick:hasUnit unit:DEG_C ." + "\n\n"

                                //$"inst:{fluidID} fpo:hasViscosity inst:{fluidViscosityID} ." + "\n" +
                                //$"inst:{fluidViscosityID} a fpo:Viscosity ." + "\n" +
                                //$"inst:{fluidViscosityID} fpo:hasValue '{fluidViscosity}'^^xsd:double ." + "\n" +
                                //$"inst:{fluidViscosityID} fpo:hasUnit 'Pascal per second'^^xsd:string ." + "\n" +

                                //$"inst:{fluidID} fpo:hasDensity inst:{fluidDensityID} ." + "\n" +
                                //$"inst:{fluidDensityID} a fpo:Density ." + "\n" +
                                //$"inst:{fluidDensityID} fpo:hasValue '{fluidDensity}'^^xsd:double ." + "\n" +
                                //$"inst:{fluidDensityID} fpo:hasUnit 'Kilograms per cubic meter'^^xsd:string ." + "\n"
                                );
                            break;
                        default:
                            break;
                    }

                    ElementSet systemComponents = system.PipingNetwork;

                    //Relate components to systems
                    foreach (Element component in systemComponents)
                    {
                        string componentID = component.UniqueId;
                        sb.Append($"inst:HydraulicSys_{systemID} fso:hasComponent inst:Comp_{componentID} ." + "\n");
                    }
                }
            }

            ////*****************

            //Get IfcExportAs components, pipe fittings, duct fittings
            FilteredElementCollector componentCollector = new FilteredElementCollector(doc);
            ICollection<Element> components = componentCollector.OfClass(typeof(FamilyInstance)).ToElements();
            List<FamilyInstance> componentList = new List<FamilyInstance>();
            foreach (FamilyInstance component in componentCollector)
            {
                string componentID = component.UniqueId.ToString();
                string revitID = component.Id.ToString();
                //Pipe fittings 
                if (component.Category.Name == "Pipe Fittings")
                {
                    //sb.Append($"inst:Comp_{componentID} props:hasGuid '{revitID}'^^xsd:string ." + "\n");
                    sb.Append($"inst:Comp_{componentID} props:hasRevitId '{revitID}'^^xsd:string ." + "\n");

                    string fittingType = ((MechanicalFitting)component.MEPModel).PartType.ToString();
                    if (fittingType.ToString() == "Tee")
                    {
                        //MaterialType
                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        string materialTypeValue = component.Name;
                        sb.Append(
                           $"inst:Comp_{componentID} a fso:Tee ;" + "\n"
                         + $"\tssn:hasProperty inst:MaterialType_{materialTypeID} ." + "\n"
                         + $"inst:MaterialType_{materialTypeID} a fpo:MaterialType ;" + "\n"
                         + $"\tbrick:value '{materialTypeValue}'^^xsd:string ." + "\n"
                         );
                    }
                    else if (fittingType.ToString() == "Elbow")
                    {
                        //MaterialType
                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        string materialTypeValue = component.Name;
                        sb.Append(
                           $"inst:Comp_{componentID} a fso:Elbow;"
                         + $"\tssn:hasProperty inst:MaterialType_{materialTypeID} ." + "\n"
                         + $"inst:MaterialType_{materialTypeID} a fpo:MaterialType ;" + "\n"
                         + $"\tbrick:value '{materialTypeValue}'^^xsd:string ." + "\n"
                         );
                        //if (component.LookupParameter("Angle") != null)
                        //{
                        //    Angle
                        //    string angleID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        //    double angleValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Angle").AsDouble(), UnitTypeId.Degrees);
                        //    sb.Append($"inst:{componentID} fpo:hasAngle inst:{angleID} ." + "\n"
                        //     + $"inst:{angleID} a fpo:Angle ." + "\n"
                        //     + $"inst:{angleID} fpo:hasValue '{angleValue}'^^xsd:double ." + "\n"
                        //     + $"inst:{angleID} fpo:hasUnit 'Degree'^^xsd:string ." + "\n");
                        //}
                    }
                    else if (fittingType.ToString() == "Transition")
                    {
                        //MaterialType
                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        string materialTypeValue = component.Name;
                        sb.Append(
                           $"inst:Comp_{componentID} a fso:Transition;"
                         + $"\tssn:hasProperty inst:MaterialType_{materialTypeID} ." + "\n"
                         + $"inst:MaterialType_{materialTypeID} a fpo:MaterialType ;" + "\n"
                         + $"\tbrick:value '{materialTypeValue}'^^xsd:string ." + "\n"
                         );

                    }

                    else if (fittingType.ToString() == "Cap")
                    {
                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        string materialTypeValue = component.Name;
                        sb.Append(

                           $"inst:Comp_{componentID} a fso:Cap;"
                         + $"\tssn:hasProperty inst:MaterialType_{materialTypeID} ." + "\n"
                         + $"inst:MaterialType_{materialTypeID} a fpo:MaterialType ;" + "\n"
                         + $"\tbrick:value '{materialTypeValue}'^^xsd:string ." + "\n"
                         );


                    }
                    else
                    {
                        sb.Append(
                                    $"inst:Comp_{componentID} a fso:Fitting." + "\n"
                                 );
                    }

                    RelatedPorts.GenericConnectors(component, revitID, componentID, sb);
                }
                //Duct fittings
                else if (component.Category.Name == "Duct Fittings")
                {
                    //sb.Append($"inst:Comp_{componentID} props:hasGuid '{revitID}'^^xsd:string ." + "\n");
                    sb.Append($"inst:Comp_{componentID} props:hasRevitId '{revitID}'^^xsd:string ." + "\n");

                    string fittingType = ((MechanicalFitting)component.MEPModel).PartType.ToString();
                    if (fittingType.ToString() == "Tee")
                    {
                        //MaterialType
                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        string materialTypeValue = component.Name;
                        sb.Append(
                           $"inst:Comp_{componentID} a fso:Tee ;" + "\n"
                         + $"\tssn:hasProperty inst:MaterialType_{materialTypeID} ." + "\n"
                         + $"inst:MaterialType_{materialTypeID} a fpo:MaterialType ;" + "\n"
                         + $"\tbrick:value '{materialTypeValue}'^^xsd:string ." + "\n"
                        );
                    }

                    else if (fittingType.ToString() == "Elbow")
                    {

                        //MaterialType
                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        string materialTypeValue = component.Name;
                        sb.Append(
                           $"inst:Comp_{componentID} a fso:Elbow;"
                         + $"\tssn:hasProperty inst:MaterialType_{materialTypeID} ." + "\n"
                         + $"inst:MaterialType_{materialTypeID} a fpo:MaterialType ;" + "\n"
                         + $"\tbrick:value '{materialTypeValue}'^^xsd:string ." + "\n"
                        );
                        /*
                        if (component.LookupParameter("Angle") != null)
                        {
                            //Angle
                            string angleID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                            double angleValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Angle").AsDouble(), UnitTypeId.Degrees);
                            sb.Append($"inst:{componentID} fpo:hasAngle inst:{angleID} ." + "\n"
                             + $"inst:{angleID} a fpo:Angle ." + "\n"
                             + $"inst:{angleID} fpo:hasValue  '{angleValue}'^^xsd:double ." + "\n"
                             + $"inst:{angleID} fpo:hasUnit  'Degree'^^xsd:string ." + "\n");
                        }
                        */
                    }

                    else if (fittingType.ToString() == "Transition")
                    {
                        //MaterialType
                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        string materialTypeValue = component.Name;
                        sb.Append(
                           $"inst:Comp_{componentID} a fso:Transition;"
                         + $"\tssn:hasProperty inst:MaterialType_{materialTypeID} ." + "\n"
                         + $"inst:MaterialType_{materialTypeID} a fpo:MaterialType ;" + "\n"
                         + $"\tbrick:value '{materialTypeValue}'^^xsd:string ." + "\n"
                         );

                        /*
                        if (component.LookupParameter("OffsetHeight") != null && component.LookupParameter("OffsetHeight").AsDouble() > 0)
                        {
                            //Length
                            string lengthID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                            double lengthValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("OffsetHeight").AsDouble(), UnitTypeId.Meters);
                            sb.Append($"inst:{componentID} fpo:hasLength inst:{lengthID} ." + "\n"
                             + $"inst:{lengthID} a fpo:Length ." + "\n"
                             + $"inst:{lengthID} fpo:hasValue '{lengthValue}'^^xsd:double ." + "\n"
                             + $"inst:{lengthID} fpo:hasUnit 'Meter'^^xsd:string ." + "\n");
                        }
                        else
                        {
                            string lengthID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                            double lengthValue = 0.02;
                            sb.Append($"inst:{componentID} fpo:hasLength inst:{lengthID} ." + "\n"
                           + $"inst:{lengthID} a fpo:Length ." + "\n"
                           + $"inst:{lengthID} fpo:hasValue '{lengthValue}'^^xsd:double ." + "\n"
                           + $"inst:{lengthID} fpo:hasUnit 'Meter'^^xsd:string ." + "\n");
                        }
                        */
                    }

                    else if (fittingType.ToString() == "Cap")
                    {
                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        string materialTypeValue = component.Name;
                        sb.Append(

                           $"inst:Comp_{componentID} a fso:Cap;"
                         + $"\tssn:hasProperty inst:MaterialType_{materialTypeID} ." + "\n"
                         + $"inst:MaterialType_{materialTypeID} a fpo:MaterialType ;" + "\n"
                         + $"\tbrick:value '{materialTypeValue}'^^xsd:string ." + "\n"
                         );


                    }

                    else if (fittingType.ToString() == "Pants")
                    {

                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        string materialTypeValue = component.Name;
                        sb.Append(
                           $"inst:Comp_{componentID} a fso:Pants;"
                         + $"\tssn:hasProperty inst:MaterialType_{materialTypeID} ." + "\n"
                         + $"inst:MaterialType_{materialTypeID} a fpo:MaterialType ;" + "\n"
                         + $"\tbrick:value '{materialTypeValue}'^^xsd:string ." + "\n"
                         );


                    }

                    else if (fittingType.ToString() == "TapAdjustable")
                    {

                        string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        string materialTypeValue = component.Name;
                        sb.Append(

                           $"inst:Comp_{componentID} a fso:Tap;"
                         + $"\tssn:hasProperty inst:MaterialType_{materialTypeID} ." + "\n"
                         + $"inst:MaterialType_{materialTypeID} a fpo:MaterialType ;" + "\n"
                         + $"\tbrick:value '{materialTypeValue}'^^xsd:string ." + "\n"
                         );


                    }

                    else
                    {

                        sb.Append(
                                    $"inst:Comp_{componentID} a fso:Fitting ." + "\n"
                                 );
                    }

                    RelatedPorts.GenericConnectors(component, revitID, componentID, sb);

                }
                else
                {
                    //IfcExportAs classes
                    ParseComponent(component, sb, doc);
                }

            }

            //Get all pipes 
            FilteredElementCollector pipeCollector = new FilteredElementCollector(doc);
            ICollection<Element> pipes = pipeCollector.OfClass(typeof(Pipe)).ToElements();
            List<Pipe> pipeList = new List<Pipe>();
            foreach (Pipe component in pipeCollector)
            {
                Pipe w = component as Pipe;

                //Type
                string componentID = component.UniqueId.ToString();
                string revitID = component.Id.ToString();
                sb.Append(
                    $"inst:{componentID} a fso:Pipe ." + "\n" +
                    $"inst:{componentID} ex:RevitID inst:{revitID} ." + "\n");

                if (component.PipeType.Roughness != null)
                {
                    //Roughness
                    string roughnessID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double rougnessValue = component.PipeType.Roughness;
                    sb.Append($"inst:{componentID} fpo:hasRoughness inst:{roughnessID} ." + "\n"
                     + $"inst:{roughnessID} a fpo:Roughness ." + "\n"
                     + $"inst:{roughnessID} fpo:hasValue '{rougnessValue}'^^xsd:double ." + "\n" +
                     $"inst:{roughnessID} fpo:hasUnit 'Meter'^^xsd:string ." + "\n");
                }
                if (component.LookupParameter("Length") != null)
                {
                    //Length
                    string lengthID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double lengthValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Length").AsDouble(), UnitTypeId.Meters);
                    sb.Append($"inst:{componentID} fpo:hasLength inst:{lengthID} ." + "\n"
                     + $"inst:{lengthID} a fpo:Length ." + "\n"
                     + $"inst:{lengthID} fpo:hasValue '{lengthValue}'^^xsd:double ." + "\n"
                     + $"inst:{lengthID} fpo:hasUnit 'Meter'^^xsd:string ." + "\n");
                }

                //MaterialType
                string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                string materialTypeValue = component.Name;
                sb.Append($"inst:{componentID} fpo:hasMaterialType inst:{materialTypeID} ." + "\n"
                 + $"inst:{materialTypeID} a fpo:MaterialType ." + "\n"
                 + $"inst:{materialTypeID} fpo:hasValue '{materialTypeValue}'^^xsd:string ." + "\n");

                RelatedPorts.GenericConnectors(component, revitID, componentID, sb);
            }

            //************************

            //Get all ducts 
            FilteredElementCollector ductCollector = new FilteredElementCollector(doc);
            ICollection<Element> ducts = ductCollector.OfClass(typeof(Duct)).ToElements();
            List<Duct> ductList = new List<Duct>();
            foreach (Duct component in ductCollector)
            {
                Duct w = component as Duct;

                //Type
                string componentID = component.UniqueId.ToString();
                string revitID = component.Id.ToString();

                sb.Append(
                    $"inst:{componentID} a fso:Duct ." + "\n" +
                    $"inst:{componentID} ex:RevitID inst:{revitID} ." + "\n");


                if (component.DuctType.Roughness != null)
                {
                    //Roughness
                    string roughnessID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double rougnessValue = component.DuctType.Roughness;
                    sb.Append($"inst:{componentID} fpo:hasRoughness inst:{roughnessID} ." + "\n"
                     + $"inst:{roughnessID} a fpo:Roughness ." + "\n"
                     + $"inst:{roughnessID} fpo:hasValue '{rougnessValue}'^^xsd:double ." + "\n" +
                     $"inst:{roughnessID} fpo:hasUnit 'Meter'^^xsd:string ." + "\n");
                }

                if (component.LookupParameter("Length") != null)
                {
                    //Length
                    string lengthID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                    double lengthValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Length").AsDouble(), UnitTypeId.Meters);
                    sb.Append($"inst:{componentID} fpo:hasLength inst:{lengthID} ." + "\n"
                     + $"inst:{lengthID} a fpo:Length ." + "\n"
                     + $"inst:{lengthID} fpo:hasValue '{lengthValue}'^^xsd:double ." + "\n"
                     + $"inst:{lengthID} fpo:hasUnit 'Meter'^^xsd:string ." + "\n");
                }

                if (component.LookupParameter("Hydraulic Diameter") != null)
                {
                    //Outside diameter
                    string outsideDiameterID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double outsideDiameterValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Hydraulic Diameter").AsDouble(), UnitTypeId.Meters);
                    sb.Append($"inst:{componentID} fpo:hasHydraulicDiameter inst:{outsideDiameterID} ." + "\n"
                     + $"inst:{outsideDiameterID} a fpo:HydraulicDiameter ." + "\n"
                     + $"inst:{outsideDiameterID} fpo:hasValue '{outsideDiameterValue}'^^xsd:double ." + "\n"
                     + $"inst:{outsideDiameterID} fpo:hasUnit 'meter'^^xsd:string ." + "\n");
                }


                //MaterialType
                string materialTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                string materialTypeValue = component.Name;
                sb.Append($"inst:{componentID} fpo:hasMaterialType inst:{materialTypeID} ." + "\n"
                 + $"inst:{materialTypeID} a fpo:MaterialType ." + "\n"
                 + $"inst:{materialTypeID} fpo:hasValue '{materialTypeValue}'^^xsd:string ." + "\n");


                if (component.LookupParameter("Loss Coefficient") != null)
                {
                    //frictionFactor 
                    string frictionFactorID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double frictionFactorValue = component.LookupParameter("Loss Coefficient").AsDouble();
                    sb.Append($"inst:{componentID} fpo:hasFrictionFactor inst:{frictionFactorID} ." + "\n"
                     + $"inst:{frictionFactorID} a fpo:FrictionFactor ." + "\n"
                     + $"inst:{frictionFactorID} fpo:hasValue '{frictionFactorValue}'^^xsd:double ." + "\n");
                }

                if (component.LookupParameter("Friction") != null)
                {
                    //friction
                    string frictionID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    double frictionIDValue = component.LookupParameter("Friction").AsDouble();
                    sb.Append($"inst:{componentID} fpo:hasFriction inst:{frictionID} ." + "\n"
                     + $"inst:{frictionID} a fpo:Friction ." + "\n"
                     + $"inst:{frictionID} fpo:hasValue '{frictionIDValue}'^^xsd:double ." + "\n"
                     + $"inst:{frictionID} fpo:hasUnit 'Pascal per meter'^^xsd:string ." + "\n");
                }

                RelatedPorts.GenericConnectors(component, revitID, componentID, sb);
            }


            //************************
            //*****************
            //Converting to string before post request
            string reader = sb.ToString();
            //Serialize the content into a text file
            System.IO.File.WriteAllText(fpath, reader);
            //Send the data to database
            var res = HttpClientHelper.POSTDataAsync(reader);
            //A task window to show everything works again
            //TaskDialog.Show("Revit", reader.ToString());

            //Postman testing
            //var client = new HttpClient();
            //var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:7200/repositories/BIM2Graph/rdf-graphs/service?default");
            //var content = new StringContent("@prefix inst: <https://example.com/inst#> .\r\n@prefix fso: <https://w3id.org/fso#> .\r\n\r\ninst:HeatExchanger-1 fso:suppliesFluidTo inst:Pipe-1 .", null, "text/turtle");
            //request.Content = content;
            //var response = client.SendAsync(request);

            return Result.Succeeded;
        }

        public Result ParseComponent(FamilyInstance component, StringBuilder sb, Autodesk.Revit.DB.Document doc)
        {
            string componentID = component.UniqueId;
            string revitID = component.Id.ToString();


            if (component.Symbol.LookupParameter("IfcExportAs") != null)
            {
                //Type
                string componentType = component.Symbol.LookupParameter("IfcExportAs").AsString();
                //Fan
                if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcFan")
                {
                    //Type 
                    sb.Append($"inst:Comp_{componentID} a fso:Fan ;" + "\n"
                        + $"\tprops:hasRevitId '{revitID}'^^xsd:string." + "\n");

                    if (component.LookupParameter("NominalPressureHead") != null)
                    {
                        //PressureRise
                        string pressureRiseID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        string pressureRiseValue = component.LookupParameter("NominalPressureHead").AsString();
                        sb.Append(
                           $"inst:Comp_{componentID} ssn:hasProperty inst:PressureRise_{pressureRiseID} ." + "\n"
                         + $"inst:PressureRise_{pressureRiseID} a fpo:NominalPressureRise ;" + "\n"
                         + $"\tbrick:value  '{pressureRiseValue}'^^xsd:string ;" + "\n"
                         + $"\tbrick:hasUnit unit:PA ." + "\n"
                        );
                    }

                    if (component.LookupParameter("NominalMassflow") != null)
                    {
                        //Massflow in Kg/s
                        string massflowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        string massflowValue = component.LookupParameter("NominalMassflow").AsString();
                        sb.Append($"inst:Comp_{componentID} ssn:hasProperty inst:Massflow_{massflowID} ." + "\n"
                         + $"inst:Massflow_{massflowID} a fpo:NominalMassflow ;" + "\n"
                         + $"\tbrick:value  '{massflowValue}'^^xsd:string ;" + "\n"
                         + $"\tbrick:hasUnit  unit:KiloGM-PER-SEC ." + "\n");
                    }
                    /*
                    if (component.LookupParameter("FSC_pressureCurve") != null)
                    {
                        //PressureCurve
                        string pressureCurveID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        string pressureCurveValue = component.LookupParameter("FSC_pressureCurve").AsString();
                        sb.Append($"inst:{componentID} fpo:hasPressureCurve inst:{pressureCurveID} ." + "\n"
                         + $"inst:{pressureCurveID} a fpo:PressureCurve ." + "\n"
                         + $"inst:{pressureCurveID} fpo:hasCurve  '{pressureCurveValue}'^^xsd:string ." + "\n"
                         + $"inst:{pressureCurveID} fpo:hasUnit  'PA:m3/h'^^xsd:string ." + "\n");
                    }

                    if (component.LookupParameter("FSC_powerCurve") != null)
                    {
                        //PowerCurve
                        string powerCurveID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        string powerCurveValue = component.LookupParameter("FSC_powerCurve").AsString();
                        sb.Append($"inst:{componentID} fpo:hasPowerCurve inst:{powerCurveID} ." + "\n"
                         + $"inst:{powerCurveID} a fpo:PowerCurve ." + "\n"
                         + $"inst:{powerCurveID} fpo:hasCurve  '{powerCurveValue}'^^xsd:string ." + "\n"
                         + $"inst:{powerCurveID} fpo:hasUnit  'PA:m3/h'^^xsd:string ." + "\n");
                    }*/

                }
                //AHU as Fan (temporary)
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcAHU")
                {
                    sb.Append($"inst:Comp_{componentID} a fso:AHU ." + "\n");

                    string componentID_supplyFan = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    string componentID_returnFan = System.Guid.NewGuid().ToString().Replace(' ', '-');

                    //create supply fan
                    //sb.Append($"inst:Comp_{componentID_supplyFan} a fso:Fan ." + "\n");

                    //create return fan
                    //sb.Append($"inst:Comp_{componentID_returnFan} a fso:Fan ." + "\n");

                    // fan feeds fluid to supply air and exhaust air
                    //return air and outside air feed fluid to fan


                    // has nested families
                    /*
                    var subComponentIds = component.GetSubComponentIds();
                    if (subComponentIds != null) {



                        foreach (var subComponentId in subComponentIds)
                        {
                            FamilyInstance subComponent = component.Document.GetElement(subComponentId) as FamilyInstance;

                            ParseComponent(subComponent, sb, doc);
                        }
                    }
                    */

                }
                //Pump
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcPump")
                {


                    string measurementID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                    string measurementType = "FlowActuator";

                    string sensorID = System.Guid.NewGuid().ToString().Replace(' ', '-');

                    //string brickSensor = "Pump";
                    string timeseriesID = "0";
                    string timeseriesRandomID = System.Guid.NewGuid().ToString().Replace(' ', '-');

                    //Type 
                    sb.Append($"inst:Comp_{componentID} a fso:Pump ;" + "\n"
                         + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n");

                    if (component.LookupParameter("NominalPressureHead") != null)
                    {
                        //PressureRise
                        string pressureRiseID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        string pressureRiseValue = component.LookupParameter("NominalPressureHead").AsString();
                        sb.Append(
                           $"inst:Comp_{componentID} ssn:hasProperty inst:PressureRise_{pressureRiseID} ." + "\n"
                         + $"inst:PressureRise_{pressureRiseID} a fpo:NominalPressureRise ;" + "\n"
                         + $"\tbrick:value  '{pressureRiseValue}'^^xsd:string ;" + "\n"
                         + $"\tbrick:hasUnit unit:PA ." + "\n"
                        );
                    }
                    if (component.LookupParameter("NominalMassflow") != null)
                    {
                        //Massflow in Kg/s
                        string massflowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        string massflowValue = component.LookupParameter("NominalMassflow").AsString();
                        sb.Append($"inst:Comp_{componentID} ssn:hasProperty inst:Massflow_{massflowID} ." + "\n"
                         + $"inst:Massflow_{massflowID} a fpo:NominalMassflow ;" + "\n"
                         + $"\tbrick:value  '{massflowValue}'^^xsd:string ;" + "\n"
                         + $"\tbrick:hasUnit  unit:KiloGM-PER-SEC ." + "\n");
                    }
                }
                //Valve
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcValve")
                {
                   //Type 
                    sb.Append($"inst:Comp_{componentID} a fso:Valve ;" + "\n"
                        + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n");

                    if (component.LookupParameter("NominalPressureDrop") != null)
                    {
                        //PressureDrop
                        string pressureDropID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        string pressureDropValue = component.LookupParameter("NominalPressureDrop").AsString();
                        sb.Append(
                           $"inst:Comp_{componentID} ssn:hasProperty inst:PressureDrop_{pressureDropID} ." + "\n"
                         + $"inst:PressureDrop_{pressureDropID} a fpo:NominalPressureDrop ;" + "\n"
                         + $"\tbrick:value  '{pressureDropValue}'^^xsd:string ;" + "\n"
                         + $"\tbrick:hasUnit unit:PA ." + "\n"
                        );
                    }

                    if (component.LookupParameter("NominalMassflow") != null)
                    {
                        //Massflow in Kg/s
                        string massflowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        string massflowValue = component.LookupParameter("NominalMassflow").AsString();
                        sb.Append($"inst:Comp_{componentID} ssn:hasProperty inst:Massflow_{massflowID} ." + "\n"
                         + $"inst:Massflow_{massflowID} a fpo:NominalMassflow ;" + "\n"
                         + $"\tbrick:value  '{massflowValue}'^^xsd:string ;" + "\n"
                         + $"\tbrick:hasUnit  unit:KiloGM-PER-SEC ." + "\n");
                    }

                }


                //Damper
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcDamper")
                {
                    //Type 
                    sb.Append(
                        $"inst:Comp_{componentID} a fso:Damper ;" + "\n"
                        + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n"
                    );
                    if (component.LookupParameter("NominalPressureDrop") != null)
                    {
                        //PressureDrop
                        string pressureDropID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        string pressureDropValue = component.LookupParameter("NominalPressureDrop").AsString();
                        sb.Append(
                           $"inst:Comp_{componentID} ssn:hasProperty inst:PressureDrop_{pressureDropID} ." + "\n"
                         + $"inst:PressureDrop_{pressureDropID} a fpo:NominalPressureDrop ;" + "\n"
                         + $"\tbrick:value  '{pressureDropValue}'^^xsd:string ;" + "\n"
                         + $"\tbrick:hasUnit unit:PA ." + "\n"
                        );
                    }

                    if (component.LookupParameter("NominalMassflow") != null)
                    {
                        //Massflow in Kg/s
                        string massflowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        string massflowValue = component.LookupParameter("NominalMassflow").AsString();
                        sb.Append($"inst:Comp_{componentID} ssn:hasProperty inst:Massflow_{massflowID} ." + "\n"
                         + $"inst:Massflow_{massflowID} a fpo:NominalMassflow ;" + "\n"
                         + $"\tbrick:value  '{massflowValue}'^^xsd:string ;" + "\n"
                         + $"\tbrick:hasUnit  unit:KiloGM-PER-SEC ." + "\n");
                    }
                }

                //Radiator
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcSpaceHeater")
                {
                    //Type
                    sb.Append($"inst:Comp_{componentID} a fso:Radiator ;" + "\n"
                        + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n");

                    //DesignHeatPower
                    if (component.Symbol.LookupParameter("NominalPower") != null)
                    {
                        string designHeatPowerID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        double designHeatPowerValue = UnitUtils.ConvertFromInternalUnits(component.Symbol.LookupParameter("NominalPower").AsDouble(), UnitTypeId.Watts);
                        if (designHeatPowerValue != 0)
                        {
                            sb.Append(
                               $"inst:Comp_{componentID} ssn:hasProperty inst:NominalHeatingPower_{designHeatPowerID} ." + "\n"
                             + $"inst:NominalHeatingPower_{designHeatPowerID} a fpo:NominalPower ;" + "\n"
                             + $"\tbrick:value '{designHeatPowerValue}'^^xsd:double ;" + "\n"
                             + $"\tbrick:hasUnit  unit:W ." + "\n"
                             );
                        }
                    }
                    //design massflow
                    if (component.Symbol.LookupParameter("NominalMassflow") != null)
                    {
                        string designMassflowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        double designMassflowValue = UnitUtils.ConvertFromInternalUnits(component.Symbol.LookupParameter("NominalMassflow").AsDouble(), UnitTypeId.LitersPerSecond);
                        sb.Append(
                           $"inst:Comp_{componentID} ssn:hasProperty inst:NominalMassflow_{designMassflowID} ." + "\n"
                         + $"inst:NominalMassflow_{designMassflowID} a fpo:NominalMassflow ;" + "\n"
                         + $"\tbrick:value '{designMassflowValue}'^^xsd:double ;" + "\n"
                         + $"\tbrick:hasUnit  unit:Lps ." + "\n"
                         );
                    }
                    if (component.Space != null)
                    {
                        //string s = component.Space.Name;
                        string relatedRoomID = component.Space.UniqueId.ToString();
                        sb.Append($"inst:Comp_{componentID} fso:transfersHeatTo inst:Space_{relatedRoomID} ." + "\n");
                    }

                }

                //AirTerminal
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcAirTerminal")
                {
                    //Type
                    sb.Append($"inst:Comp_{componentID} a fso:AirTerminal ;" + "\n"
                        + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n");

                    if (component.LookupParameter("System Classification").AsString() == "Return Air")
                    {
                        //AirTerminalType
                        string airTerminalTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        sb.Append(
                           $"inst:Comp_{componentID} ssn:hasProperty inst:AirTerminalType_{airTerminalTypeID} ." + "\n"
                         + $"inst:AirTerminalType_{airTerminalTypeID} a fpo:AirTerminalType_Outlet ." + "\n"
                         );

                        if (component.Space != null)
                        {
                            //Relation to room and space
                            string relatedRoomID = component.Space.UniqueId.ToString();
                            sb.Append($"inst:Space_{relatedRoomID} fso:suppliesFluidTo inst:Comp_{componentID} ." + "\n");
                        }
                        else if (component.Room != null)
                        {
                            string relatedRoomID = component.Room.UniqueId.ToString();

                            sb.Append($"inst:Space_{relatedRoomID} a bot:Space ." + "\n" +
                                $"inst:Space_{relatedRoomID} fso:suppliesFluidTo inst:Comp_{componentID} ." + "\n"
                                );
                        }
                        //Adding a fictive port the airterminal which is not included in Revit
                        string connectorID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        sb.Append(
                            $"inst:Comp_{componentID} fso:hasPort inst:Port_{connectorID} ." + "\n"
                            + $"inst:Port_{connectorID} a fso:Port ." + "\n"
                            );

                        //Diameter to fictive port 

                        //FlowDirection to fictive port
                        string connectorDirectionID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        //string connectorDirection = "In";

                        sb.Append(
                          $"inst:Comp_{connectorID} ssn:hasProperty inst:FlowDirection_{connectorDirectionID} ." + "\n"
                        + $"inst:FlowDirection_{connectorDirectionID} a fpo:NominalFlowDirection_In ." + "\n"
                        );
                    }


                    if (component.LookupParameter("System Classification").AsString() == "Supply Air")
                    {
                        //AirTerminalType
                        string airTerminalTypeID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        // string airTerminalTypeValue = "inlet";
                        sb.Append(
                           $"inst:Comp_{componentID} ssn:hasProperty inst:AirTerminalType_{airTerminalTypeID} ." + "\n"
                         + $"inst:AirTerminalType_{airTerminalTypeID} a fpo:AirTerminalType_Inlet ." + "\n"
                         );
                        if (component.Space != null)
                        {
                            //Relation to room and space
                            string relatedRoomID = component.Space.UniqueId.ToString();
                            //sb.Append($"inst:Space_{relatedRoomID} fso:suppliesFluidTo inst:Comp_{componentID} ." + "\n");
                            sb.Append($"inst:Comp_{componentID} fso:suppliesFluidTo inst:Space_{relatedRoomID} ." + "\n");
                        }

                        else if (component.Room != null)
                        {
                            //Relation to room and space
                            string relatedRoomID = component.Room.UniqueId.ToString();
                            sb.Append($"inst:Comp_{componentID} fso:suppliesFluidTo inst:Space_{relatedRoomID} ." + "\n");
                        }

                        //Adding a fictive port the airterminal which is not included in Revit
                        string connectorID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        sb.Append(
                              $"inst:Comp_{componentID} fso:hasPort inst:Port_{connectorID} ." + "\n"
                            + $"inst:Port_{connectorID} a fso:Port ." + "\n"
                        );

                        //FlowDirection
                        string connectorDirectionID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        // string connectorDirection = "Out";

                        sb.Append(
                          $"inst:Comp_{connectorID} ssn:hasProperty inst:FlowDirection_{connectorDirectionID} ." + "\n"
                        + $"inst:FlowDirection_{connectorDirectionID} a fpo:NominalFlowDirection_Out ." + "\n"
                        );


                        //Fictive pressureDrop
                        string pressureDropID = System.Guid.NewGuid().ToString().Replace(' ', '-'); ;
                        double pressureDropValue = 5;
                        sb.Append($"inst:{connectorID} ssn:hasProperty inst:PressureDrop_{pressureDropID} ." + "\n"
                       + $"inst:PressureDrop_{pressureDropID} a fpo:NominalPressureDrop ;" + "\n"
                       + $"\tbrick:value '{pressureDropValue}'^^xsd:double ;" + "\n"
                       + $"\tbrick:hasUnit unit:PA ." + "\n");

                        //if (component.LookupParameter("Flow") != null)
                        //{
                        //    //Flow rate
                        //    string flowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        //    double flowValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("Flow").AsDouble(), UnitTypeId.LitersPerSecond);
                        //    sb.Append($"inst:{connectorID} fpo:flowRate inst:{flowID} ." + "\n"
                        //     + $"inst:{flowID} a fpo:FlowRate ." + "\n"
                        //     + $"inst:{flowID} fpo:hasValue '{flowValue}'^^xsd:double ." + "\n"
                        //     + $"inst:{flowID} fpo:hasUnit 'Liters per second'^^xsd:string ." + "\n");
                        //}
                    }
                }

                //Heat exchanger
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcHeatExchanger")
                {
                    sb.Append($"inst:Comp_{componentID} a fso:HeatExchanger ;" + "\n"
                        + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n");

                    if (component.LookupParameter("NominalPower") != null)
                    {
                        //DesignHeatPower
                        string designHeatPowerID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        double designHeatPowerValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("NominalPower").AsDouble(), UnitTypeId.Watts);
                        sb.Append($"inst:Comp_{componentID} ssn:hasProperty inst:NominalHeatingPower_{designHeatPowerID} ." + "\n"
                         + $"inst:NominalHeatingPower_{designHeatPowerID} a fpo:NominalPower ;" + "\n"
                         + $"\tbrick:value  '{designHeatPowerValue}'^^xsd:double ;" + "\n"
                         + $"\tbrick:hasUnit  unit:W ." + "\n");
                    }
                }

                // heating distributor
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcDistributionElement")
                {
                    //Type
                    sb.Append($"inst:Comp_{componentID} a fso:DistributionElement ;" + "\n"
                        + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n");
                    //DesignHeatPower
                    if (component.LookupParameter("NominalHeatingPower") != null)
                    {
                        string designHeatPowerID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        double designHeatPowerValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("NominalHeatingPower").AsDouble(), UnitTypeId.Watts);
                        sb.Append(
                           $"inst:Comp_{componentID} ssn:hasProperty inst:NominalHeatingPower_{designHeatPowerID} ." + "\n"
                         + $"inst:NominalHeatingPower_{designHeatPowerID} a fpo:NominalPower ;" + "\n"
                         + $"\tbrick:value '{designHeatPowerValue}'^^xsd:double ;" + "\n"
                         + $"\tbrick:hasUnit  unit:W ." + "\n"
                         );
                    }


                }

                // boiler
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcBoiler")
                {

                    //Type
                    sb.Append($"inst:Comp_{componentID} a fso:Boiler ;" + "\n"
                        + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n");

                    //DesignHeatPower
                    if (component.LookupParameter("NominalPower") != null)
                    {
                        string designHeatPowerID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        double designHeatPowerValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("NominalPower").AsDouble(), UnitTypeId.Watts);
                        sb.Append(
                           $"inst:Comp_{componentID} ssn:hasProperty inst:NominalHeatingPower_{designHeatPowerID} ." + "\n"
                         + $"inst:NominalHeatingPower_{designHeatPowerID} a fpo:NominalPower ;" + "\n"
                         + $"\tbrick:value '{designHeatPowerValue}'^^xsd:double ;" + "\n"
                         + $"\tbrick:hasUnit  unit:W ." + "\n"
                         );
                    }


                    //design massflow
                    if (component.LookupParameter("NominalMassflow") != null)
                    {
                        string designMassflowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        double designMassflowValue = UnitUtils.ConvertFromInternalUnits(component.LookupParameter("NominalMassflow").AsDouble(), UnitTypeId.LitersPerSecond);
                        sb.Append(
                           $"inst:Comp_{componentID} ssn:hasProperty inst:NominalMassflow_{designMassflowID} ." + "\n"
                         + $"inst:NominalMassflow_{designMassflowID} a fpo:NominalMassflow ;" + "\n"
                         + $"\tbrick:value '{designMassflowValue}'^^xsd:double ;" + "\n"
                         + $"\tbrick:hasUnit  unit:Lps ." + "\n"
                         );
                    }

                    //design massflow
                    if (component.LookupParameter("Efficiency") != null)
                    {
                        string designEfficiencyID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        double designEfficiencyValue = component.LookupParameter("Efficiency").AsDouble();
                        sb.Append(
                           $"inst:Comp_{componentID} ssn:hasProperty inst:Efficiency_{designEfficiencyID} ." + "\n"
                         + $"inst:Efficiency_{designEfficiencyID} a fpo:Efficiency ;" + "\n"
                         + $"\tbrick:value '{designEfficiencyValue}'^^xsd:double ;" + "\n"
                         + $"\tbrick:hasUnit  unit:Lps ." + "\n"
                         );
                    }


                }

                //FireDamper
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcFireDamper")
                {
                    //Type 
                    sb.Append(
                      $"inst:Comp_{componentID} a fso:FireDamper ;" + "\n"
                      + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n"
                    //+ $"fso:{componentType} rdfs:subClassOf fpo:Damper ." + "\n"
                    );
                    if (component.LookupParameter("NominalPressureDrop") != null)
                    {
                        //PressureDrop
                        string pressureDropID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        string pressureDropValue = component.LookupParameter("NominalPressureDrop").AsString();
                        sb.Append(
                           $"inst:Comp_{componentID} ssn:hasProperty inst:PressureDrop_{pressureDropID} ." + "\n"
                         + $"inst:PressureDrop_{pressureDropID} a fpo:NominalPressureDrop ;" + "\n"
                         + $"\tbrick:value  '{pressureDropValue}'^^xsd:string ;" + "\n"
                         + $"\tbrick:hasUnit unit:PA ." + "\n"
                        );
                    }

                    if (component.LookupParameter("NominalMassflow") != null)
                    {
                        //Massflow in Kg/s
                        string massflowID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        string massflowValue = component.LookupParameter("NominalMassflow").AsString();
                        sb.Append($"inst:Comp_{componentID} ssn:hasProperty inst:Massflow_{massflowID} ." + "\n"
                         + $"inst:Massflow_{massflowID} a fpo:NominalMassflow ;" + "\n"
                         + $"\tbrick:value  '{massflowValue}'^^xsd:string ;" + "\n"
                         + $"\tbrick:hasUnit  unit:KiloGM-PER-SEC ." + "\n");
                    }
                    /*
                    if (component.LookupParameter("FSC_kv") != null)
                    {
                        //Kv
                        string kvID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        double kvValue = component.LookupParameter("FSC_kv").AsDouble();
                        sb.Append($"inst:{componentID} fpo:hasKv inst:{kvID} ." + "\n"
                         + $"inst:{kvID} a fpo:Kv ." + "\n"
                         + $"inst:{kvID} fpo:hasValue  '{kvValue}'^^xsd:double ." + "\n");
                    }

                    if (component.LookupParameter("FSC_kvs") != null)
                    {
                        //Kvs
                        string kvsID = System.Guid.NewGuid().ToString().Replace(' ', '-');
                        double kvsValue = component.LookupParameter("FSC_kvs").AsDouble();
                        sb.Append($"inst:{componentID} fpo:hasKvs inst:{kvsID} ." + "\n"
                         + $"inst:{kvsID} a fpo:Kvs ." + "\n"
                         + $"inst:{kvsID} fpo:hasValue  '{kvsValue}'^^xsd:double ." + "\n");
                    }
                    */
                }

                //DuctSilencer
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcDuctSilencer")
                {
                    //Type 
                    sb.Append(
                      $"inst:Comp_{componentID} a fso:DuctSilencer ;" + "\n"
                      + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n"
                    );
                }

                //Controlled Valve
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcControlledValve")
                {
                    //Type
                    sb.Append($"inst:Comp_{componentID} a fso:ControlledValve ;" + "\n"
                        + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n");
                }

                //SensorFitting
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcSensorFitting")
                {
                    //Type
                    sb.Append($"inst:Comp_{componentID} a fso:SensorFitting ;" + "\n"
                        + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n");
                }

                //Flowmeter
                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcFlowmeter")
                {
                    //Type
                    sb.Append($"inst:Comp_{componentID} a fso:Flowmeter ;" + "\n"
                        + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n");
                }

                else if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcAHUFan")
                {
                    // do noting
                }

                else
                {
                    sb.Append($"inst:Comp_{componentID} a fso:Component ;" + "\n"
                         + $"\tprops:hasRevitId '{revitID}'^^xsd:string ." + "\n");
                }

                //if (component.Symbol.LookupParameter("IfcExportAs").AsString() == "IfcAHU")
                //{
                RelatedPorts.GenericConnectors(component, revitID, componentID, sb);
                //}
            }

            return Result.Succeeded;
        }

    }
}
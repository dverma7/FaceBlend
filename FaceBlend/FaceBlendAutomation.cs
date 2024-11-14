using System;
using System.Reflection;
using log4net;
using NXOpen;
using NXOpen.Features;

[assembly: log4net.Config.XmlConfigurator(Watch = true)]
namespace FaceBlend
{
    public class FaceBlendAutomation
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static NXOpen.Session theSession = NXOpen.Session.GetSession();
        public static NXOpen.Part workPart = theSession.Parts.Work;

        public static void Main(string[] args)
        {
            log.Info("FaceBlendAutomation started.");

            try
            {
                // Set undo mark for the operation
                NXOpen.Session.UndoMarkId markId1 = theSession.SetUndoMark(NXOpen.Session.MarkVisibility.Visible, "Start Face Blend");
                log.Info("Undo mark set for the face blend operation.");

                // Get the face blend radius from user input
                double blendRadius = GetFaceBlendRadiusFromUser();

                if (blendRadius <= 0)
                {
                    log.Warn("Invalid blend radius entered. Operation canceled.");
                    theSession.ListingWindow.WriteLine("Invalid blend radius input. Operation canceled.");
                    return;
                }

                log.Info($"Entered blend radius: {blendRadius} mm.");

                // Automate selection of two faces by prompting the user
                SelectionHelper selectionHelper = new SelectionHelper();
                var faceSelections = selectionHelper.SelectTwoFaces(workPart);

                // Call the CreateFaceBlend method to perform the operation
                bool blendCreated = CreateFaceBlend(faceSelections, blendRadius);

                if (blendCreated)
                {
                    log.Info($"Face blend created successfully with radius {blendRadius} mm.");
                    theSession.ListingWindow.WriteLine($"Face blend created successfully with radius {blendRadius} mm.");
                }
                else
                {
                    log.Warn("Face blend creation failed.");
                    theSession.ListingWindow.WriteLine("Face blend creation failed.");
                }
            }
            catch (NXException ex)
            {
                log.Error("NXException occurred during the face blend operation.", ex);
                theSession.ListingWindow.Open();
                theSession.ListingWindow.WriteLine($"NXException occurred: {ex.Message}");
            }
            catch (Exception ex)
            {
                log.Error("An unexpected exception occurred.", ex);
                theSession.ListingWindow.Open();
                theSession.ListingWindow.WriteLine($"Exception occurred: {ex.Message}");
            }
        }

        // Function to create a face blend
        public static bool CreateFaceBlend(NXOpen.ScCollector[] faceSelections, double blendRadius)
        {
            try
            {
                log.Info("Initializing FaceBlendBuilder...");
                NXOpen.Features.FaceBlendBuilder faceBlendBuilder = workPart.Features.CreateFaceBlendBuilder(null);

                faceBlendBuilder.CircularCrossSection.RadiusOption = NXOpen.GeometricUtilities.RadiusMethod.Constant;
                faceBlendBuilder.CircularCrossSection.SetLawControlConstantRadius(blendRadius.ToString());
                faceBlendBuilder.CircularCrossSection.Radius.RightHandSide = blendRadius.ToString();

                // Assign selected faces to FaceBlendBuilder collectors
                faceBlendBuilder.FirstFaceCollector = faceSelections[0];
                faceBlendBuilder.SecondFaceCollector = faceSelections[1];
                faceBlendBuilder.CrossSectionType = NXOpen.Features.FaceBlendBuilder.CrossSectionOption.Circular;

                faceBlendBuilder.SewAllFaces = true;
                faceBlendBuilder.TrimInputFacesToBlendFaces = true;
                faceBlendBuilder.RemoveSelfIntersections = true;

                faceBlendBuilder.ReverseFirstFaceNormal = true;
                faceBlendBuilder.ReverseSecondFaceNormal = true;

                bool blendCreated = false;
                while (!blendCreated && blendRadius > 0.1)
                {
                    try
                    {
                        log.Info($"Attempting to create face blend with radius {blendRadius} mm.");
                        faceBlendBuilder.CircularCrossSection.SetLawControlConstantRadius(blendRadius.ToString());
                        NXOpen.Features.Feature faceBlendFeature = faceBlendBuilder.CommitFeature();
                        blendCreated = true;
                        log.Info("Face blend created successfully.");
                    }
                    catch (NXException ex)
                    {
                        if (ex.ErrorCode == 1050027) // Error code for incompatible blend size
                        {
                            blendRadius -= 0.5;
                            log.Warn($"Reducing blend radius to {blendRadius} mm and retrying due to incompatible blend size.");
                        }
                        else
                        {
                            log.Error("NXException occurred during face blend creation.", ex);
                            throw;
                        }
                    }
                }

                faceBlendBuilder.Destroy();
                return blendCreated;
            }
            catch (Exception ex)
            {
                log.Error("Error during face blend creation.", ex);
                theSession.ListingWindow.WriteLine($"Error during face blend creation: {ex.Message}");
                return false;
            }
        }

        // Function to get face blend radius from the user using an input box
        public static double GetFaceBlendRadiusFromUser()
        {
            double radius = 0.0;
            bool validInput = false;

            while (!validInput)
            {
                string input = Microsoft.VisualBasic.Interaction.InputBox("Enter radius for face blend (in mm):", "Face Blend Radius Input", "5.0");

                if (double.TryParse(input, out radius) && radius > 0)
                {
                    validInput = true;
                    log.Info($"User entered blend radius: {radius} mm.");
                }
                else
                {
                    log.Warn("Invalid input for radius.");
                    NXOpen.UI.GetUI().NXMessageBox.Show("Invalid Input", NXOpen.NXMessageBox.DialogType.Error,
                        "Please enter a valid positive number for the radius.");
                }
            }

            return radius;
        }

        // Helper class for selecting faces
        public class SelectionHelper
        {
            public NXOpen.ScCollector[] SelectTwoFaces(NXOpen.Part workPart)
            {
                NXOpen.Session theSession = NXOpen.Session.GetSession();
                NXOpen.UI theUI = NXOpen.UI.GetUI();
                TaggedObject[] selectedObjects;

                string prompt = "Please select two faces for the face blend operation";
                string title = "Face Selection";
                NXOpen.Select.FilterMember[] filterMembers = new NXOpen.Select.FilterMember[] {
                    NXOpen.Select.FilterMember.AllFaces
                };

                var selectionResult = theUI.SelectionManager.SelectTaggedObjectsWithFilterMembers(
                    prompt, title, NXOpen.Selection.SelectionScope.AnyInAssembly, NXOpen.Selection.SelectionAction.ClearAndEnableSpecific,
                    filterMembers, out selectedObjects);

                if (selectionResult == NXOpen.Selection.Response.Cancel || selectedObjects.Length < 2)
                {
                    log.Warn("Selection canceled or insufficient number of faces selected.");
                    throw new Exception("Selection canceled or insufficient number of faces selected.");
                }

                NXOpen.ScCollector[] faceCollectors = new NXOpen.ScCollector[2];
                for (int i = 0; i < 2; i++)
                {
                    faceCollectors[i] = workPart.ScCollectors.CreateCollector();
                    NXOpen.Face selectedFace = selectedObjects[i] as NXOpen.Face;
                    if (selectedFace == null)
                    {
                        log.Error("Invalid selection. Please select a valid face.");
                        throw new Exception("Invalid selection. Please select a valid face.");
                    }

                    NXOpen.SelectionIntentRule faceRule = workPart.ScRuleFactory.CreateRuleFaceDumb(new NXOpen.Face[] { selectedFace });
                    faceCollectors[i].ReplaceRules(new NXOpen.SelectionIntentRule[] { faceRule }, false);
                }

                log.Info("Two faces selected for face blend.");
                return faceCollectors;
            }
        }

        // Required method for unloading the NXOpen DLL
        public static int GetUnloadOption(string dummy)
        {
            return (int)NXOpen.Session.LibraryUnloadOption.Immediately;
        }
    }
}



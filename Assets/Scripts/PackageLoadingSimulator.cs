using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class ContainerData
{
    public float width = 4.0f;
    public float height = 3.0f;
    public float length = 6.0f;
    public float maxWeight = 500.0f;
}

[Serializable]
public class PackageData
{
    public string id;
    public float width;
    public float height;
    public float length;
    public float weight;
}

[Serializable]
public class LoadingInputData
{
    public ContainerData container;
    public List<PackageData> packages;
}

[Serializable]
public class ExportedPackageState
{
    public string id;
    public float weight;
    public bool isLoadedInTruck;
    public bool isValidPlacement;
    public Vector3 position;
    public Vector3 rotationEuler;
    public Vector3 dimensions;
}

[Serializable]
public class LoadingOutputData
{
    public ContainerData container;
    public float totalLoadedWeight;
    public bool isArrangementValid;
    public List<ExportedPackageState> packages = new List<ExportedPackageState>();
}

public class PackageItem : MonoBehaviour
{
    public PackageData data;
    public Renderer meshRenderer;
    public BoxCollider boxCollider;

    public bool isInsideContainer;
    public bool isOutOfBounds;
    public bool isOverlapping;
    public bool isWeightExceeded;

    public Bounds WorldBounds
    {
        get { return meshRenderer.bounds; }
    }

    public bool IsValidInTruck
    {
        get { return isInsideContainer && !isOutOfBounds && !isOverlapping && !isWeightExceeded; }
    }
}

public struct UndoRecord
{
    public PackageItem package;
    public Vector3 position;
    public Quaternion rotation;
}

public class PackageLoadingSimulator : MonoBehaviour
{
    private LoadingInputData inputData;
    private Bounds containerBounds;
    private List<PackageItem> packageItems = new List<PackageItem>();
    private Stack<UndoRecord> undoStack = new Stack<UndoRecord>();

    private PackageItem selectedPackage;
    private bool isDragging = false;
    private Plane dragPlane;
    private Vector3 dragOffset;
    private Vector3 dragStartPos;
    private Quaternion dragStartRot;

    private float totalLoadedWeight = 0f;
    private string statusMessage = "Ready. Click any package to select and drag into the truck.";
    private Material baseMaterial;

    // Camera Orbit Controls
    private Vector3 cameraTarget = Vector3.zero;
    private float camDistance = 12f;
    private float camYaw = -35f;
    private float camPitch = 30f;

    void Start()
    {
        SetupBaseMaterial();
        LoadInputJson();
        BuildScene();
        UpdateCameraPosition();
        ValidateAllPackages();
    }

    void SetupBaseMaterial()
    {
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseMaterial = new Material(temp.GetComponent<Renderer>().sharedMaterial);
        Destroy(temp);
    }

    Material CreateColoredMaterial(Color color)
    {
        Material mat = new Material(baseMaterial);
        mat.color = color;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        return mat;
    }

    void LoadInputJson()
    {
        string folderPath = Application.streamingAssetsPath;
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        string filePath = Path.Combine(folderPath, "input.json");

        if (!File.Exists(filePath))
        {
            // Agar input.json na mile toh sample file apne aap generate ho jayegi
            inputData = new LoadingInputData
            {
                container = new ContainerData { width = 4f, height = 3f, length = 6f, maxWeight = 500f },
                packages = new List<PackageData>
                {
                    new PackageData { id = "PKG_01", width = 1.5f, height = 1.0f, length = 1.5f, weight = 120f },
                    new PackageData { id = "PKG_02", width = 1.2f, height = 1.2f, length = 1.2f, weight = 90f },
                    new PackageData { id = "PKG_03", width = 2.0f, height = 1.0f, length = 1.5f, weight = 150f },
                    new PackageData { id = "PKG_04", width = 1.0f, height = 1.5f, length = 1.0f, weight = 80f },
                    new PackageData { id = "PKG_05", width = 1.5f, height = 1.0f, length = 2.0f, weight = 110f },
                    new PackageData { id = "PKG_06", width = 1.0f, height = 1.0f, length = 1.0f, weight = 60f }
                }
            };
            File.WriteAllText(filePath, JsonUtility.ToJson(inputData, true));
        }
        else
        {
            string jsonContent = File.ReadAllText(filePath);
            inputData = JsonUtility.FromJson<LoadingInputData>(jsonContent);
        }
    }

    void BuildScene()
    {
        ContainerData c = inputData.container;
        Vector3 containerCenter = new Vector3(0f, c.height / 2f, 0f);
        Vector3 containerSize = new Vector3(c.width, c.height, c.length);
        containerBounds = new Bounds(containerCenter, containerSize);
        cameraTarget = new Vector3(1.5f, c.height * 0.4f, 0f);

        // 1. Ground Plane
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0, -0.06f, 0);
        ground.transform.localScale = new Vector3(3f, 1f, 3f);
        ground.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.18f, 0.2f, 0.23f));
        Destroy(ground.GetComponent<Collider>());

        // 2. Truck Container Floor
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "TruckContainerFloor";
        floor.transform.position = new Vector3(0, -0.025f, 0);
        floor.transform.localScale = new Vector3(c.width, 0.05f, c.length);
        floor.GetComponent<Renderer>().material = CreateColoredMaterial(new Color(0.28f, 0.32f, 0.38f));
        Destroy(floor.GetComponent<Collider>());

        // 3. Truck Wireframe Boundaries (12 Edges)
        CreateContainerWireframe(containerBounds, new Color(0.3f, 0.75f, 1f));

        // 4. Generate Packages in Staging Area (Right Side of Truck)
        float startX = (c.width / 2f) + 2.2f;
        float startZ = -(c.length / 2f);
        float currentZ = startZ;
        float rowX = startX;

        for (int i = 0; i < inputData.packages.Count; i++)
        {
            PackageData p = inputData.packages[i];
            GameObject boxObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boxObj.name = p.id;
            boxObj.transform.localScale = new Vector3(p.width, p.height, p.length);

            if (currentZ + p.length > (c.length / 2f) + 2f)
            {
                currentZ = startZ;
                rowX += 2.5f;
            }

            Vector3 spawnPos = new Vector3(rowX, p.height / 2f, currentZ + (p.length / 2f));
            currentZ += p.length + 0.6f;

            boxObj.transform.position = spawnPos;

            PackageItem item = boxObj.AddComponent<PackageItem>();
            item.data = p;
            item.meshRenderer = boxObj.GetComponent<Renderer>();
            item.boxCollider = boxObj.GetComponent<BoxCollider>();
            item.meshRenderer.material = CreateColoredMaterial(new Color(0.82f, 0.62f, 0.38f));

            packageItems.Add(item);
        }
    }

    void CreateContainerWireframe(Bounds b, Color frameColor)
    {
        GameObject frameParent = new GameObject("TruckContainerWireframe");
        float thickness = 0.04f;
        Material frameMat = CreateColoredMaterial(frameColor);

        Vector3 min = b.min;
        Vector3 max = b.max;

        // 4 Vertical Pillars
        CreateBeam(frameParent.transform, new Vector3(min.x, b.center.y, min.z), new Vector3(thickness, b.size.y, thickness), frameMat);
        CreateBeam(frameParent.transform, new Vector3(max.x, b.center.y, min.z), new Vector3(thickness, b.size.y, thickness), frameMat);
        CreateBeam(frameParent.transform, new Vector3(min.x, b.center.y, max.z), new Vector3(thickness, b.size.y, thickness), frameMat);
        CreateBeam(frameParent.transform, new Vector3(max.x, b.center.y, max.z), new Vector3(thickness, b.size.y, thickness), frameMat);

        // 4 Top Beams
        CreateBeam(frameParent.transform, new Vector3(b.center.x, max.y, min.z), new Vector3(b.size.x, thickness, thickness), frameMat);
        CreateBeam(frameParent.transform, new Vector3(b.center.x, max.y, max.z), new Vector3(b.size.x, thickness, thickness), frameMat);
        CreateBeam(frameParent.transform, new Vector3(min.x, max.y, b.center.z), new Vector3(thickness, thickness, b.size.z), frameMat);
        CreateBeam(frameParent.transform, new Vector3(max.x, max.y, b.center.z), new Vector3(thickness, thickness, b.size.z), frameMat);

        // 4 Bottom Beams
        CreateBeam(frameParent.transform, new Vector3(b.center.x, min.y, min.z), new Vector3(b.size.x, thickness, thickness), frameMat);
        CreateBeam(frameParent.transform, new Vector3(b.center.x, min.y, max.z), new Vector3(b.size.x, thickness, thickness), frameMat);
        CreateBeam(frameParent.transform, new Vector3(min.x, min.y, b.center.z), new Vector3(thickness, thickness, b.size.z), frameMat);
        CreateBeam(frameParent.transform, new Vector3(max.x, min.y, b.center.z), new Vector3(thickness, thickness, b.size.z), frameMat);
    }

    void CreateBeam(Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.transform.SetParent(parent);
        beam.transform.position = pos;
        beam.transform.localScale = scale;
        beam.GetComponent<Renderer>().material = mat;
        Destroy(beam.GetComponent<Collider>());
    }

    void Update()
    {
        HandleCameraControls();
        HandleMouseInteraction();
        HandleKeyboardShortcuts();
    }

    void HandleCameraControls()
    {
        if (Input.GetMouseButton(1))
        {
            camYaw += Input.GetAxis("Mouse X") * 4f;
            camPitch -= Input.GetAxis("Mouse Y") * 4f;
            camPitch = Mathf.Clamp(camPitch, 10f, 80f);
            UpdateCameraPosition();
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f && !Input.GetKey(KeyCode.LeftShift))
        {
            camDistance = Mathf.Clamp(camDistance - scroll * 5f, 5f, 25f);
            UpdateCameraPosition();
        }
    }

    void UpdateCameraPosition()
    {
        if (Camera.main == null) return;
        Quaternion rot = Quaternion.Euler(camPitch, camYaw, 0f);
        Camera.main.transform.position = cameraTarget + rot * new Vector3(0f, 0f, -camDistance);
        Camera.main.transform.LookAt(cameraTarget);
    }

    void HandleMouseInteraction()
    {
        if (GUIUtility.hotControl != 0) return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                PackageItem hitItem = hit.collider.GetComponent<PackageItem>();
                if (hitItem != null)
                {
                    selectedPackage = hitItem;
                    isDragging = true;
                    dragStartPos = selectedPackage.transform.position;
                    dragStartRot = selectedPackage.transform.rotation;

                    dragPlane = new Plane(Vector3.up, selectedPackage.transform.position);
                    if (dragPlane.Raycast(ray, out float enter))
                    {
                        dragOffset = selectedPackage.transform.position - ray.GetPoint(enter);
                    }
                    ValidateAllPackages();
                }
            }
        }

        if (isDragging && selectedPackage != null && Input.GetMouseButton(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (dragPlane.Raycast(ray, out float enter))
            {
                Vector3 targetPos = ray.GetPoint(enter) + dragOffset;
                // Snap to 0.1m grid for clean arrangement
                targetPos.x = Mathf.Round(targetPos.x * 10f) / 10f;
                targetPos.z = Mathf.Round(targetPos.z * 10f) / 10f;
                targetPos.y = selectedPackage.transform.position.y;

                selectedPackage.transform.position = targetPos;
                ValidateAllPackages();
            }
        }

        if (isDragging && Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            if (selectedPackage != null && Vector3.Distance(dragStartPos, selectedPackage.transform.position) > 0.01f)
            {
                undoStack.Push(new UndoRecord
                {
                    package = selectedPackage,
                    position = dragStartPos,
                    rotation = dragStartRot
                });
            }
        }
    }

    void HandleKeyboardShortcuts()
    {
        if (selectedPackage != null)
        {
            float step = 0.25f;
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) MoveSelected(new Vector3(0, 0, step));
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) MoveSelected(new Vector3(0, 0, -step));
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) MoveSelected(new Vector3(-step, 0, 0));
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) MoveSelected(new Vector3(step, 0, 0));
            if (Input.GetKeyDown(KeyCode.E)) MoveSelected(new Vector3(0, step, 0));
            if (Input.GetKeyDown(KeyCode.Q)) MoveSelected(new Vector3(0, -step, 0));

            if (Input.GetKeyDown(KeyCode.R)) RotateSelected(new Vector3(90, 0, 0));
            if (Input.GetKeyDown(KeyCode.T)) RotateSelected(new Vector3(0, 90, 0));
            if (Input.GetKeyDown(KeyCode.Y)) RotateSelected(new Vector3(0, 0, 90));
            if (Input.GetKeyDown(KeyCode.Space)) DropSelectedToFloorOrStack();
        }

        if (Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || !isDragging))
        {
            PerformUndo();
        }
    }

    public void MoveSelected(Vector3 delta)
    {
        if (selectedPackage == null) return;
        RecordUndo(selectedPackage);
        Vector3 newPos = selectedPackage.transform.position + delta;
        float halfHeight = selectedPackage.WorldBounds.extents.y;
        if (newPos.y < halfHeight) newPos.y = halfHeight;
        selectedPackage.transform.position = newPos;
        ValidateAllPackages();
    }

    public void RotateSelected(Vector3 eulerDelta)
    {
        if (selectedPackage == null) return;
        RecordUndo(selectedPackage);
        selectedPackage.transform.Rotate(eulerDelta, Space.World);

        // Floor ke niche jane se rokein
        float halfHeight = selectedPackage.WorldBounds.extents.y;
        Vector3 pos = selectedPackage.transform.position;
        if (pos.y < halfHeight)
        {
            pos.y = halfHeight;
            selectedPackage.transform.position = pos;
        }
        ValidateAllPackages();
    }

    public void DropSelectedToFloorOrStack()
    {
        if (selectedPackage == null) return;
        RecordUndo(selectedPackage);

        Bounds myBounds = selectedPackage.WorldBounds;
        float targetBottomY = 0f;

        foreach (var other in packageItems)
        {
            if (other == selectedPackage) continue;
            Bounds ob = other.WorldBounds;

            bool overlapXZ = (myBounds.min.x < ob.max.x - 0.02f) && (myBounds.max.x > ob.min.x + 0.02f) &&
                             (myBounds.min.z < ob.max.z - 0.02f) && (myBounds.max.z > ob.min.z + 0.02f);

            if (overlapXZ && ob.max.y <= myBounds.min.y + 0.5f)
            {
                if (ob.max.y > targetBottomY)
                    targetBottomY = ob.max.y;
            }
        }

        Vector3 pos = selectedPackage.transform.position;
        pos.y = targetBottomY + myBounds.extents.y;
        selectedPackage.transform.position = pos;
        ValidateAllPackages();
    }

    void RecordUndo(PackageItem item)
    {
        undoStack.Push(new UndoRecord
        {
            package = item,
            position = item.transform.position,
            rotation = item.transform.rotation
        });
    }

    public void PerformUndo()
    {
        if (undoStack.Count > 0)
        {
            UndoRecord rec = undoStack.Pop();
            rec.package.transform.position = rec.position;
            rec.package.transform.rotation = rec.rotation;
            selectedPackage = rec.package;
            statusMessage = "Undo applied for " + rec.package.data.id;
            ValidateAllPackages();
        }
        else
        {
            statusMessage = "Nothing left to Undo.";
        }
    }

    public void ValidateAllPackages()
    {
        totalLoadedWeight = 0f;
        float eps = 0.02f; // Minor floating-point tolerance

        // 1. Check container zones & boundaries
        foreach (var p in packageItems)
        {
            Bounds pb = p.WorldBounds;
            bool intersectsTruckZone = containerBounds.Intersects(pb);

            if (!intersectsTruckZone && pb.min.x > containerBounds.max.x + 0.3f)
            {
                // Package abhi bahar Staging Area mein hai
                p.isInsideContainer = false;
                p.isOutOfBounds = false;
            }
            else
            {
                p.isInsideContainer = true;
                bool fullyInside =
                    pb.min.x >= containerBounds.min.x - eps &&
                    pb.max.x <= containerBounds.max.x + eps &&
                    pb.min.y >= containerBounds.min.y - eps &&
                    pb.max.y <= containerBounds.max.y + eps &&
                    pb.min.z >= containerBounds.min.z - eps &&
                    pb.max.z <= containerBounds.max.z + eps;

                p.isOutOfBounds = !fullyInside;
                totalLoadedWeight += p.data.weight;
            }

            p.isOverlapping = false;
            p.isWeightExceeded = false;
        }

        // 2. Check package-to-package overlaps
        for (int i = 0; i < packageItems.Count; i++)
        {
            for (int j = i + 1; j < packageItems.Count; j++)
            {
                Bounds b1 = packageItems[i].WorldBounds;
                Bounds b2 = packageItems[j].WorldBounds;

                // Shrink slightly so touching surfaces are valid
                b1.Expand(-eps * 2f);
                b2.Expand(-eps * 2f);

                if (b1.Intersects(b2))
                {
                    packageItems[i].isOverlapping = true;
                    packageItems[j].isOverlapping = true;
                }
            }
        }

        // 3. Check total weight capacity
        bool overweight = totalLoadedWeight > inputData.container.maxWeight;
        foreach (var p in packageItems)
        {
            if (p.isInsideContainer && overweight)
            {
                p.isWeightExceeded = true;
            }
        }

        // 4. Apply Visual Feedback Colors
        foreach (var p in packageItems)
        {
            Color targetColor;
            if (!p.isInsideContainer)
            {
                targetColor = p.isOverlapping ? new Color(0.9f, 0.2f, 0.2f) : new Color(0.82f, 0.62f, 0.38f);
            }
            else if (p.isOutOfBounds || p.isOverlapping || p.isWeightExceeded)
            {
                targetColor = new Color(0.92f, 0.18f, 0.18f); // RED: Invalid Placement
            }
            else
            {
                targetColor = new Color(0.18f, 0.82f, 0.32f); // GREEN: Valid Placement
            }

            // Highlight selected box slightly brighter
            if (p == selectedPackage)
            {
                targetColor = Color.Lerp(targetColor, Color.yellow, 0.28f);
            }

            p.meshRenderer.material.color = targetColor;
            if (p.meshRenderer.material.HasProperty("_BaseColor"))
                p.meshRenderer.material.SetColor("_BaseColor", targetColor);
        }
    }

    public void ExportArrangementToJson()
    {
        LoadingOutputData output = new LoadingOutputData();
        output.container = inputData.container;
        output.totalLoadedWeight = totalLoadedWeight;
        output.isArrangementValid = totalLoadedWeight <= inputData.container.maxWeight;

        foreach (var p in packageItems)
        {
            if (p.isInsideContainer && !p.IsValidInTruck)
                output.isArrangementValid = false;

            output.packages.Add(new ExportedPackageState
            {
                id = p.data.id,
                weight = p.data.weight,
                isLoadedInTruck = p.isInsideContainer,
                isValidPlacement = p.IsValidInTruck,
                position = p.transform.position,
                rotationEuler = p.transform.eulerAngles,
                dimensions = new Vector3(p.data.width, p.data.height, p.data.length)
            });
        }

        string outPath = Path.Combine(Application.streamingAssetsPath, "output.json");
        string json = JsonUtility.ToJson(output, true);
        File.WriteAllText(outPath, json);

        statusMessage = "Exported successfully to: StreamingAssets/output.json";
        Debug.Log("Exported JSON:\n" + json);
    }

    void OnGUI()
    {
        GUI.Box(new Rect(15, 15, 360, 410), "Interactive Package Loading Simulator");

        int y = 45;
        GUI.Label(new Rect(30, y, 330, 25), $"Container Size: {inputData.container.width}m x {inputData.container.height}m x {inputData.container.length}m");
        y += 25;

        bool overWeight = totalLoadedWeight > inputData.container.maxWeight;
        GUI.color = overWeight ? Color.red : Color.green;
        GUI.Label(new Rect(30, y, 330, 25), $"Loaded Weight: {totalLoadedWeight} / {inputData.container.maxWeight} kg " + (overWeight ? "[EXCEEDED!]" : "[OK]"));
        GUI.color = Color.white;
        y += 30;

        if (selectedPackage != null)
        {
            GUI.Label(new Rect(30, y, 330, 25), $"Selected: {selectedPackage.data.id} ({selectedPackage.data.weight} kg)");
            y += 22;

            string stateStr = "Staging Area (Outside Truck)";
            if (selectedPackage.isInsideContainer)
            {
                if (selectedPackage.IsValidInTruck) stateStr = "VALID (Inside Truck)";
                else
                {
                    List<string> errs = new List<string>();
                    if (selectedPackage.isOutOfBounds) errs.Add("Out of Bounds");
                    if (selectedPackage.isOverlapping) errs.Add("Overlap");
                    if (selectedPackage.isWeightExceeded) errs.Add("Overweight");
                    stateStr = "INVALID: " + string.Join(", ", errs);
                }
            }
            GUI.Label(new Rect(30, y, 330, 25), $"Status: {stateStr}");
            y += 30;

            GUI.Label(new Rect(30, y, 330, 22), "Rotate Selected Package (90°):");
            y += 24;
            if (GUI.Button(new Rect(30, y, 100, 28), "Rot X (R)")) RotateSelected(new Vector3(90, 0, 0));
            if (GUI.Button(new Rect(140, y, 100, 28), "Rot Y (T)")) RotateSelected(new Vector3(0, 90, 0));
            if (GUI.Button(new Rect(250, y, 100, 28), "Rot Z (Y)")) RotateSelected(new Vector3(0, 0, 90));
            y += 36;

            GUI.Label(new Rect(30, y, 330, 22), "Height / Vertical Controls:");
            y += 24;
            if (GUI.Button(new Rect(30, y, 100, 28), "Move Up (E)")) MoveSelected(new Vector3(0, 0.25f, 0));
            if (GUI.Button(new Rect(140, y, 100, 28), "Move Down (Q)")) MoveSelected(new Vector3(0, -0.25f, 0));
            if (GUI.Button(new Rect(250, y, 100, 28), "Drop/Stack")) DropSelectedToFloorOrStack();
            y += 40;
        }
        else
        {
            GUI.Label(new Rect(30, y, 330, 50), "Click any box in the scene to select.\nLeft-Drag to move, Right-Drag to orbit camera.");
            y += 116;
        }

        if (GUI.Button(new Rect(30, y, 155, 35), $"Undo ({undoStack.Count}) [Z]"))
        {
            PerformUndo();
        }

        if (GUI.Button(new Rect(195, y, 155, 35), "Export JSON"))
        {
            ExportArrangementToJson();
        }
        y += 45;

        GUI.Label(new Rect(30, y, 330, 45), statusMessage);
    }
}
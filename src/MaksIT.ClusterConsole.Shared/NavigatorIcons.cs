namespace MaksIT.ClusterConsole.Shared;

public static class NavigatorIcons {
  public static string Path(string section) => section switch {
    ResourceCatalog.Cluster => "M4,2 H20 V7 H4 Z M6,4 H8 V5 H6 Z M4,9 H20 V14 H4 Z M6,11 H8 V12 H6 Z M4,16 H20 V21 H4 Z M6,18 H8 V19 H6 Z",
    ResourceCatalog.Nodes => "M2,4 H22 V16 H2 Z M7,18 H17 V21 H7 Z",
    ResourceCatalog.Applications => "M3,3 H10 V10 H3 Z M14,3 H21 V10 H14 Z M3,14 H10 V21 H3 Z M14,14 H21 V21 H14 Z",
    ResourceCatalog.Workloads => "M2,8 L12,3 L22,8 L12,13 Z M4,11 L12,15 L20,11 L20,13.5 L12,17.5 L4,13.5 Z M4,16 L12,20 L20,16 L20,18.5 L12,22.5 L4,18.5 Z",
    ResourceCatalog.Config => "M10,2 H14 L15.5,5.5 L19.5,4.5 L21.5,8 L18.5,10 L21.5,12 L19.5,15.5 L15.5,14.5 L14,18 H10 L8.5,14.5 L4.5,15.5 L2.5,12 L5.5,10 L2.5,8 L4.5,4.5 L8.5,5.5 Z M12,8 A2.5,2.5 0 1 0 12,13 A2.5,2.5 0 1 0 12,8 Z",
    ResourceCatalog.Network => "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 Z M2,11 H22 V13 H2 Z",
    ResourceCatalog.Storage => "M4,6 C4,3.8 7.6,2 12,2 C16.4,2 20,3.8 20,6 C20,8.2 16.4,10 12,10 C7.6,10 4,8.2 4,6 Z M4,6 V16 C4,18.2 7.6,20 12,20 C16.4,20 20,18.2 20,16 V6",
    ResourceCatalog.Namespaces => "M2,7 H9 L11,9 H22 V20 H2 Z M4,4 H10 V7 H4 Z",
    ResourceCatalog.Events => "M12,2 C8.5,2 6,6 6,10 V15 L3,19 H21 L18,15 V10 C18,6 15.5,2 12,2 Z M10,20 H14 C14,21.6 13.1,22.5 12,22.5 C10.9,22.5 10,21.6 10,20 Z",
    ResourceCatalog.Helm => "M11,3 H13 V11 H21 V13 H13 V21 H11 V13 H3 V11 H11 Z",
    ResourceCatalog.Dapr => "M8,3 H16 L21,12 L16,21 H8 L3,12 Z",
    ResourceCatalog.AccessControl => "M12,2 L21,6 V12 C21,17 12,22 12,22 C12,22 3,17 3,12 V6 Z",
    ResourceCatalog.CustomResources => "M9,3 H13 V6 H17 V10 H20 V14 H17 V18 H13 V21 H9 V18 H5 V14 H8 V10 H5 V6 H9 Z",
    _ => "M5,5 H19 V19 H5 Z"
  };
}

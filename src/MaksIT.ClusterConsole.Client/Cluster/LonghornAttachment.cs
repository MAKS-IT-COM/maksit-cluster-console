using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Client.Cluster;

/// <summary>
/// Longhorn UI attach and detach update the <c>longhorn-ui</c> ticket on
/// <c>volumeattachments.longhorn.io</c>, and <c>spec.nodeID</c> for managers that still read it.
/// </summary>
internal static class LonghornAttachment {
  public const string TicketId = "longhorn-ui";
  public const string Group = "longhorn.io";
  public const string Version = "v1beta2";
  public const string Plural = "volumeattachments";
  public const string VolumePlural = "volumes";

  public static readonly ResourceRef Attachment = new(Group, Version, Plural, "VolumeAttachment", true);

  public static readonly ResourceRef Volume = new(Group, Version, VolumePlural, "Volume", true);

  public static string TicketMerge(string nodeId, bool disableFrontend) =>
    new JsonObject {
      ["spec"] = new JsonObject {
        ["attachmentTickets"] = new JsonObject {
          [TicketId] = Ticket(nodeId, disableFrontend)
        }
      }
    }.ToJsonString();

  public static string ClearTickets(IEnumerable<string> ticketIds) {
    var tickets = new JsonObject();

    foreach (var id in ticketIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
      tickets[id] = JsonNode.Parse("null");

    if (!tickets.ContainsKey(TicketId))
      tickets[TicketId] = JsonNode.Parse("null");

    return new JsonObject {
      ["spec"] = new JsonObject {
        ["attachmentTickets"] = tickets
      }
    }.ToJsonString();
  }

  public static string NodePatch(string nodeId) =>
    new JsonObject {
      ["spec"] = new JsonObject {
        ["nodeID"] = nodeId
      }
    }.ToJsonString();

  public static JsonObject CreateBody(string name, string @namespace, string nodeId, bool disableFrontend) =>
    new() {
      ["apiVersion"] = Group + "/" + Version,
      ["kind"] = "VolumeAttachment",
      ["metadata"] = new JsonObject {
        ["name"] = name,
        ["namespace"] = @namespace
      },
      ["spec"] = new JsonObject {
        ["volume"] = name,
        ["attachmentTickets"] = new JsonObject {
          [TicketId] = Ticket(nodeId, disableFrontend)
        }
      }
    };

  public static IReadOnlyList<string> TicketIds(JsonObject? attachment) {
    if (attachment?["spec"]?["attachmentTickets"] is not JsonObject tickets)
      return [];

    return tickets.Select(ticket => ticket.Key).ToList();
  }

  private static JsonObject Ticket(string nodeId, bool disableFrontend) =>
    new() {
      ["id"] = TicketId,
      ["type"] = "longhorn-api",
      ["nodeID"] = nodeId,
      ["parameters"] = new JsonObject {
        ["disableFrontend"] = disableFrontend ? "true" : "false",
        ["lastAttachedBy"] = ""
      }
    };
}

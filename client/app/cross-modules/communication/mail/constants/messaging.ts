import type { IMessagingServiceData } from "../models/messaging";

export const messagingServiceData: IMessagingServiceData[] = [
  {
    id: "1",
    name: "Reset Password",
    configuration: "Default",
    protocol: "SMS",
    lastModified: new Date("2024-02-19T10:42:00"),
    createdOn: new Date("2024-02-19"),
    createdBy: "Randy Franci",
  },
  {
    id: "2",
    name: "Choosing Appeals and Possible Outcomes",
    configuration: "Custom Configuration 1",
    protocol: "SMS",
    lastModified: new Date("2024-02-19T10:42:00"),
    createdOn: new Date("2024-02-19"),
    createdBy: "Chance Press",
  },
  {
    id: "3",
    name: "Take Risks (E2)",
    configuration: "Default",
    protocol: "WhatsApp",
    lastModified: new Date("2024-02-19T10:42:00"),
    createdOn: new Date("2024-02-19"),
    createdBy: "Makenna George",
  },
  {
    id: "4",
    name: "Influence Skills Equal Better Leaders",
    configuration: "Custom Configuration 1",
    protocol: "SMS",
    lastModified: new Date("2024-02-19T10:42:00"),
    createdOn: new Date("2024-02-19"),
    createdBy: "Jordyn Stanton",
  },
  {
    id: "5",
    name: "Delegation Power",
    configuration: "Custom Configuration 4",
    protocol: "SMS",
    lastModified: new Date("2024-02-19T10:42:00"),
    createdOn: new Date("2024-02-19"),
    createdBy: "Miracle Passaquindici Arcand",
  },
  {
    id: "6",
    name: "People are All Different",
    configuration: "Default",
    protocol: "SMS",
    lastModified: new Date("2024-02-19T10:42:00"),
    createdOn: new Date("2024-02-19"),
    createdBy: "Aspen Stanton",
  },
  {
    id: "7",
    name: "Tactics for Influencing: The Hands",
    configuration: "Custom Configuration 8",
    protocol: "WhatsApp",
    lastModified: new Date("2024-02-19T10:42:00"),
    createdOn: new Date("2024-02-19"),
    createdBy: "Kadin Ekstrom Bothman",
  },
  {
    id: "8",
    name: "Self-Assessment",
    configuration: "Default",
    protocol: "WhatsApp",
    lastModified: new Date("2024-02-19T10:42:00"),
    createdOn: new Date("2024-02-19"),
    createdBy: "Jaylon Rhiel Madsen",
  },
  {
    id: "9",
    name: "Listening to Understand",
    configuration: "Custom Configuration 1",
    protocol: "WhatsApp",
    lastModified: new Date("2024-02-19T10:42:00"),
    createdOn: new Date("2024-02-19"),
    createdBy: "Cristofer Bator",
  },
  {
    id: "10",
    name: "Tactics for Influencing: The Head",
    configuration: "Custom Configuration 8",
    protocol: "SMS",
    lastModified: new Date("2024-02-19T10:42:00"),
    createdOn: new Date("2024-02-19"),
    createdBy: "Madelyn Ekstrom Bothman",
  },
  {
    id: "11",
    name: "Tactics for Influencing: The Heart",
    configuration: "Custom Configuration 3",
    protocol: "Email",
    lastModified: new Date("2024-03-01T12:30:00"),
    createdOn: new Date("2024-03-01"),
    createdBy: "Elliot Stoneburner",
  },
  {
    id: "12",
    name: "Leadership Through Empathy",
    configuration: "System Mail",
    protocol: "SMS",
    lastModified: new Date("2024-03-10T14:45:00"),
    createdOn: new Date("2024-03-09"),
    createdBy: "Kara Molitor",
  },
];

export const configurations = [
  {
    value: "Custom Configuration 1",
    label: "Custom Configuration 1",
  },
  {
    value: "Custom Configuration 4",
    label: "Custom Configuration 4",
  },
  {
    value: "Custom Configuration 8",
    label: "Custom Configuration 8",
  },
  {
    value: "Default",
    label: "Default",
  },
];

export const protocols = [
  {
    label: "SMS",
    value: "SMS",
  },
  {
    label: "WhatsApp",
    value: "WhatsApp",
  },
];

export const EMAIL_TABS = {
  Emailstemplates: { label: "Templates" },
  Inbox: { label: "Incoming Mails" },
  Outgoingmails: { label: "Outgoing Mails" },
} as const;

export type EmailTabKey = keyof typeof EMAIL_TABS;

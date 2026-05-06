import NotificationConfigurationList from "@blocks-utilities/notification/pages/notification-list";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";

export default function NotificationPage() {
  return (
    <div className="flex flex-col gap-6 p-6">
      <PageBreadcrumb />
      <div className="flex w-full justify-between text-high-emphasis">
        <h3 className="text-2xl font-bold tracking-tight">Notifications</h3>
      </div>
      <NotificationConfigurationList />
    </div>
  );
}

import { ManagedServices } from "@blocks-identifier/pages/services/managed-services";

export default function ManagedServicesPage() {
  return (
    <main className="flex flex-col gap-6 p-6">
      <div className="flex flex-col justify-between gap-4">
        <div>
          <h1 className="text-xl font-semibold md:text-2xl">Managed Services</h1>
          <p className="text-muted-foreground">
            Register and monitor your services with logs and traces.
          </p>
        </div>
      </div>
      <ManagedServices />
    </main>
  );
}

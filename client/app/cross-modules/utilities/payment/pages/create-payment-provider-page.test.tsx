import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CreatePaymentProviderPage } from "./create-payment-provider-page";

const { registerProvider, useGetOrganizations } = vi.hoisted(() => ({
  registerProvider: vi.fn(),
  useGetOrganizations: vi.fn(),
}));

vi.mock("../hooks/use-register-payment-provider", () => ({
  useRegisterPaymentProvider: () => ({
    mutateAsync: registerProvider,
    isPending: false,
  }),
}));

vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations,
}));

const toastMock = vi.fn();

vi.mock("@/hooks/use-toast", () => ({
  toast: (...args: unknown[]) => toastMock(...args),
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));

const organization = (itemId: string, name: string) => ({
  itemId,
  name,
  isEnable: true,
  createdDate: "",
  lastUpdatedDate: "",
  createdBy: "",
  lastUpdatedBy: "",
  language: null,
  organizationIds: [],
  tags: [],
});

/** What the register hook resolves to: one outcome per organization it configured. */
const registered = (organizationIds: (string | null)[]) => ({
  providerName: "STRIPE",
  allSucceeded: true,
  organizations: organizationIds.map((organizationId, index) => ({
    organizationId,
    isSuccess: true,
    status: "REGISTERED" as const,
    paymentProviderId: `p-${index + 1}`,
    errorCode: null,
    errorMessage: null,
  })),
});

const withOrganizations = (organizations: ReturnType<typeof organization>[]) =>
  useGetOrganizations.mockReturnValue({
    data: { organizations, errors: null, isSuccess: true, totalCount: organizations.length },
    isError: false,
  });

const fillRequiredStripeFields = async (
  user: ReturnType<typeof userEvent.setup>,
) => {
  const provider = screen.getAllByRole("combobox")[0];
  await user.click(provider);
  await user.click(await screen.findByRole("option", { name: "Stripe Checkout" }));

  await user.type(screen.getByLabelText("Merchant ID"), "acct_123");
  await user.type(screen.getByLabelText("API key"), "sk_test_123");
  await user.type(
    screen.getByLabelText("Webhook endpoint secret"),
    "whsec_abc",
  );

  // The field defaults to window.location.origin, which is http under jsdom, and the
  // schema requires an absolute HTTPS URL. Left as-is, submission is blocked and the
  // test fails for a reason that has nothing to do with organizations.
  const resultUrl = screen.getByLabelText("Frontend result URL");
  await user.clear(resultUrl);
  await user.type(resultUrl, "https://app.example/payment/result");
};

describe("CreatePaymentProviderPage organization selection", () => {
  it("omits the organization entirely when none is chosen", async () => {
    const user = userEvent.setup();
    withOrganizations([organization("org-2", "Retail")]);
    registerProvider.mockResolvedValue(registered(["org-2"]));

    render(
      <MemoryRouter>
        <CreatePaymentProviderPage />
      </MemoryRouter>,
    );

    // The default names no organization, which must send nothing at all — an empty string
    // would be a real organization id as far as the server is concerned. Naming none is what
    // makes the configuration serve every organization that has none of its own.
    expect(screen.getAllByRole("combobox")[1]).toHaveTextContent(
      "Every organization in this tenant",
    );

    await fillRequiredStripeFields(user);
    await user.click(screen.getByRole("button", { name: /create provider/i }));

    await waitFor(() =>
      expect(registerProvider).toHaveBeenCalledWith(
        expect.objectContaining({ organizationId: undefined }),
      ),
    );
  });

  it("sends the chosen organization", async () => {
    const user = userEvent.setup();
    withOrganizations([
      organization("org-2", "Retail"),
      organization("org-3", "Wholesale"),
    ]);
    registerProvider.mockResolvedValue(registered(["org-2"]));

    render(
      <MemoryRouter>
        <CreatePaymentProviderPage />
      </MemoryRouter>,
    );

    const organizationSelect = screen.getAllByRole("combobox")[1];
    await user.click(organizationSelect);
    await user.click(await screen.findByRole("option", { name: "Wholesale" }));

    await fillRequiredStripeFields(user);
    await user.click(screen.getByRole("button", { name: /create provider/i }));

    await waitFor(() =>
      expect(registerProvider).toHaveBeenCalledWith(
        expect.objectContaining({ organizationId: "org-3" }),
      ),
    );
  });

  it("still allows registration when the organization list cannot be loaded", async () => {
    useGetOrganizations.mockReturnValue({ data: undefined, isError: true });

    render(
      <MemoryRouter>
        <CreatePaymentProviderPage />
      </MemoryRouter>,
    );

    // IAM being down must not block registering against the caller's own organization,
    // which is what every registration did before the selector existed.
    expect(
      screen.getByText(/Organizations could not be loaded/i),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /create provider/i }),
    ).toBeEnabled();
  });

  /**
   * A tenant whose organizations all bill through one merchant account would otherwise repeat
   * the whole registration, credentials included, once per organization.
   */
  it("configures every additionally selected organization", async () => {
    const user = userEvent.setup();
    withOrganizations([
      organization("org-2", "Retail"),
      organization("org-3", "Wholesale"),
    ]);
    registerProvider.mockResolvedValue(registered(["org-2", "org-3"]));

    render(
      <MemoryRouter>
        <CreatePaymentProviderPage />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("checkbox", { name: "Retail" }));
    await user.click(screen.getByRole("checkbox", { name: "Wholesale" }));

    await fillRequiredStripeFields(user);
    await user.click(screen.getByRole("button", { name: /create provider/i }));

    await waitFor(() =>
      expect(registerProvider).toHaveBeenCalledWith(
        expect.objectContaining({ organizationIds: ["org-2", "org-3"] }),
      ),
    );
  });

  /**
   * Omitted rather than sent empty, so a registration that names no extra organization is the
   * same request it has always been.
   */
  it("omits the list entirely when no extra organization is selected", async () => {
    const user = userEvent.setup();
    withOrganizations([organization("org-2", "Retail")]);
    registerProvider.mockResolvedValue(registered([null]));

    render(
      <MemoryRouter>
        <CreatePaymentProviderPage />
      </MemoryRouter>,
    );

    await fillRequiredStripeFields(user);
    await user.click(screen.getByRole("button", { name: /create provider/i }));

    await waitFor(() =>
      expect(registerProvider).toHaveBeenCalledWith(
        expect.objectContaining({ organizationIds: undefined }),
      ),
    );
  });

  /**
   * The organization chosen above is already being configured, so offering it again as an
   * extra would show it twice and invite a selection the server would just de-duplicate.
   */
  it("does not offer the chosen organization as an extra as well", async () => {
    const user = userEvent.setup();
    withOrganizations([
      organization("org-2", "Retail"),
      organization("org-3", "Wholesale"),
    ]);

    render(
      <MemoryRouter>
        <CreatePaymentProviderPage />
      </MemoryRouter>,
    );

    await user.click(screen.getAllByRole("combobox")[1]);
    await user.click(await screen.findByRole("option", { name: "Wholesale" }));

    expect(
      screen.queryByRole("checkbox", { name: "Wholesale" }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Retail" })).toBeInTheDocument();
  });

  /**
   * Partial success is a real outcome, not an error: what succeeded is configured and staying.
   * Reporting it as a failure would invite a retry that then conflicts on every organization
   * that already worked.
   */
  it("reports a partial success without calling it a failure", async () => {
    const user = userEvent.setup();
    withOrganizations([
      organization("org-2", "Retail"),
      organization("org-3", "Wholesale"),
    ]);
    registerProvider.mockResolvedValue({
      providerName: "STRIPE",
      allSucceeded: false,
      organizations: [
        {
          organizationId: "org-2",
          isSuccess: true,
          status: "REGISTERED" as const,
          paymentProviderId: "p-1",
          errorCode: null,
          errorMessage: null,
        },
        {
          organizationId: "org-3",
          isSuccess: false,
          status: "FAILED" as const,
          paymentProviderId: null,
          errorCode: "payment_key_ring_unavailable",
          errorMessage: "The encryption key ring could not be provisioned.",
        },
      ],
    });

    render(
      <MemoryRouter>
        <CreatePaymentProviderPage />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole("checkbox", { name: "Retail" }));
    await user.click(screen.getByRole("checkbox", { name: "Wholesale" }));
    await fillRequiredStripeFields(user);
    await user.click(screen.getByRole("button", { name: /create provider/i }));

    await waitFor(() =>
      expect(toastMock).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "warning" }),
      ),
    );
  });

  /**
   * The webhook URL is the one part of setup that happens outside this console, and getting it
   * wrong fails quietly: the provider accepts the configuration, the shopper completes the
   * payment, and nothing ever tells this service, so the payment stays in Processing.
   */
  it("shows the webhook endpoint to register with the provider", () => {
    withOrganizations([]);

    render(
      <MemoryRouter>
        <CreatePaymentProviderPage />
      </MemoryRouter>,
    );

    // Adyen is the default selection, and it needs both notification endpoints — a merchant
    // who registers only the standard one never receives saved-card events.
    expect(
      screen.getByText(/\/payments\/adyen\/webhooks\/standard$/),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/\/payments\/adyen\/webhooks\/tokens$/),
    ).toBeInTheDocument();
  });

  it("switches the webhook endpoint when the provider changes", async () => {
    const user = userEvent.setup();
    withOrganizations([]);

    render(
      <MemoryRouter>
        <CreatePaymentProviderPage />
      </MemoryRouter>,
    );

    await user.click(screen.getAllByRole("combobox")[0]);
    await user.click(
      await screen.findByRole("option", { name: "Stripe Checkout" }),
    );

    expect(
      screen.getByText(/\/payments\/stripe\/webhooks$/),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/adyen\/webhooks/),
    ).not.toBeInTheDocument();
  });
});

/**
 * The payment method selection. Absent unless Stripe is the chosen provider, since it is Stripe's
 * own concept and Adyen ignores both fields.
 */
describe("CreatePaymentProviderPage checkout payment methods", () => {
  const hex64 = "0123456789abcdef".repeat(4);

  // The blocks above share these mocks without resetting them, so calls accumulate across the
  // file. Reading the first recorded call would read some earlier test's submission.
  beforeEach(() => {
    registerProvider.mockReset();
    toastMock.mockReset();
  });

  const renderPage = () => {
    withOrganizations([]);
    registerProvider.mockResolvedValue(registered([null]));

    return render(
      <MemoryRouter>
        <CreatePaymentProviderPage />
      </MemoryRouter>,
    );
  };

  const chooseProvider = async (
    user: ReturnType<typeof userEvent.setup>,
    name: string,
  ) => {
    await user.click(screen.getAllByRole("combobox")[0]);
    await user.click(await screen.findByRole("option", { name }));
  };

  const fillRequiredAdyenFields = async (
    user: ReturnType<typeof userEvent.setup>,
  ) => {
    await user.type(screen.getByLabelText("Merchant ID"), "MyMerchant");
    await user.type(screen.getByLabelText("API key"), "adyen-key");
    await user.type(screen.getByLabelText("Standard webhook HMAC"), hex64);
    await user.type(screen.getByLabelText("Token webhook HMAC"), hex64);

    const resultUrl = screen.getByLabelText("Frontend result URL");
    await user.clear(resultUrl);
    await user.type(resultUrl, "https://app.example/payment/result");
  };

  const create = (user: ReturnType<typeof userEvent.setup>) =>
    user.click(screen.getByRole("button", { name: /create provider/i }));

  const submitted = () => registerProvider.mock.calls[0][0];

  it("does not offer them for the provider that has no such concept", () => {
    renderPage();

    // Adyen is the default choice, so this is also the state the page opens in.
    expect(screen.queryByRole("checkbox", { name: "Card" })).toBeNull();
    expect(
      screen.queryByLabelText(/Payment method configuration ID/),
    ).toBeNull();
  });

  it("sends the methods that were ticked", async () => {
    const user = userEvent.setup();
    renderPage();
    await fillRequiredStripeFields(user);

    await user.click(screen.getByRole("checkbox", { name: "Card" }));
    await user.click(screen.getByRole("checkbox", { name: "TWINT" }));
    await create(user);

    await waitFor(() => expect(registerProvider).toHaveBeenCalled());
    expect(submitted().checkoutPaymentMethodTypes).toEqual(["card", "twint"]);
  });

  /**
   * Stripe renders the methods in the order they arrive, and a checkbox list shows nothing of the
   * order they were ticked in — so the order submitted is the order on screen, and clicking the
   * same two boxes the other way round is the same configuration.
   */
  it("submits them in the order shown rather than the order ticked", async () => {
    const user = userEvent.setup();
    renderPage();
    await fillRequiredStripeFields(user);

    await user.click(screen.getByRole("checkbox", { name: "TWINT" }));
    await user.click(screen.getByRole("checkbox", { name: "Card" }));
    await create(user);

    await waitFor(() => expect(registerProvider).toHaveBeenCalled());
    expect(submitted().checkoutPaymentMethodTypes).toEqual(["card", "twint"]);
  });

  /**
   * Omitted rather than empty. An empty list is not a checkout offering nothing — the server reads
   * it as never having been set, which is what every provider registered before this form did.
   */
  it("omits the list when nothing was ticked", async () => {
    const user = userEvent.setup();
    renderPage();
    await fillRequiredStripeFields(user);

    await create(user);

    await waitFor(() => expect(registerProvider).toHaveBeenCalled());
    expect(submitted().checkoutPaymentMethodTypes).toBeUndefined();
    expect(submitted().paymentMethodConfigurationId).toBeUndefined();
  });

  it("sends a Dashboard configuration id when no method was ticked", async () => {
    const user = userEvent.setup();
    renderPage();
    await fillRequiredStripeFields(user);

    await user.type(
      screen.getByLabelText(/Payment method configuration ID/),
      "pmc_123",
    );
    await create(user);

    await waitFor(() => expect(registerProvider).toHaveBeenCalled());
    expect(submitted().paymentMethodConfigurationId).toBe("pmc_123");
  });

  it("refuses a configuration id that is not one", async () => {
    const user = userEvent.setup();
    renderPage();
    await fillRequiredStripeFields(user);

    await user.type(
      screen.getByLabelText(/Payment method configuration ID/),
      "card",
    );
    await create(user);

    expect(
      await screen.findByText(
        "A Stripe payment method configuration id starts with pmc_.",
      ),
    ).toBeTruthy();
    expect(registerProvider).not.toHaveBeenCalled();
  });

  /** Switching provider drops the selection, so it cannot be carried into one that ignores it. */
  it("forgets the selection when the provider changes", async () => {
    const user = userEvent.setup();
    renderPage();

    await chooseProvider(user, "Stripe Checkout");
    await user.click(screen.getByRole("checkbox", { name: "Card" }));
    expect(screen.getByRole("checkbox", { name: "Card" })).toBeChecked();

    await chooseProvider(user, "Adyen Hosted Checkout");
    expect(screen.queryByRole("checkbox", { name: "Card" })).toBeNull();

    await chooseProvider(user, "Stripe Checkout");
    expect(screen.getByRole("checkbox", { name: "Card" })).not.toBeChecked();
  });

  it("sends neither field for a provider that ignores them", async () => {
    const user = userEvent.setup();
    renderPage();

    await chooseProvider(user, "Stripe Checkout");
    await user.click(screen.getByRole("checkbox", { name: "Card" }));
    await chooseProvider(user, "Adyen Hosted Checkout");

    await fillRequiredAdyenFields(user);
    await create(user);

    await waitFor(() => expect(registerProvider).toHaveBeenCalled());
    expect(submitted().providerName).toBe("ADYEN-ONLINE");
    expect(submitted().checkoutPaymentMethodTypes).toBeUndefined();
    expect(submitted().paymentMethodConfigurationId).toBeUndefined();
  });

  /** Ticking one of these changes nothing about a subscription, so the form says so. */
  it("marks the methods that cannot back a renewal", async () => {
    const user = userEvent.setup();
    renderPage();

    await chooseProvider(user, "Stripe Checkout");

    expect(screen.getAllByText("one-off payments only")).toHaveLength(2);
  });
});

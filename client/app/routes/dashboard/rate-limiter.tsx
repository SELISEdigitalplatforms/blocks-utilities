import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui-kits/card/card";

export default function RateLimiterPage() {
	return (
		<main className="flex flex-col gap-6 p-6">
			<div>
				<h1 className="text-xl font-semibold md:text-2xl">Rate Limiter</h1>
				<p className="text-muted-foreground">Configure rate limiting policies for your API</p>
			</div>

			<Card>
				<CardHeader>
					{/* <CardTitle>Rate Limiter</CardTitle>
					<CardDescription>Set up rate limiting rules and thresholds</CardDescription> */}
				</CardHeader>
				<CardContent className="flex h-40 items-center justify-center text-muted-foreground">
					Rate Limiter content coming soon...
				</CardContent>
			</Card>
		</main>
	);
}

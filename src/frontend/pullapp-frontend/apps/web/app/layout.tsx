// apps/web/app/layout.tsx
import type { Metadata } from 'next';
import { Navbar } from './components/Navbar';
import '../styles/global.css';
import { ApiInitializer } from './components/ApiInitializer';

export const metadata: Metadata = {
	title: 'PullApp - Wspólne dojazdy',
	description: 'Dziel się kosztami podróży na codziennych trasach',
};

export default function RootLayout({
	children,
}: {
	children: React.ReactNode;
}) {
	return (
		<html lang="pl">
		<body>
		{/* Odpala się po stronie klienta i konfiguruje Axiosa */}
		<ApiInitializer />
		
		{/* Navbar wyrenderuje się na samej górze każdej strony */}
		<Navbar />
		
		{/* W tym miejscu Next.js automatycznie wstrzykuje zawartość z plików page.tsx */}
		<main>
			{children}
		</main>
		</body>
		</html>
	);
}
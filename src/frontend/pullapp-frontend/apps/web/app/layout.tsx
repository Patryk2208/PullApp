// apps/web/app/layout.tsx
import type { Metadata } from 'next';
import { Navbar } from './components/Navbar';
import '../styles/global.css';

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
		{/* 1. Navbar wyrenderuje się na samej górze każdej strony */}
		<Navbar />
		
		{/* 2. W tym miejscu Next.js automatycznie wstrzykuje zawartość z plików page.tsx */}
		<main>
			{children}
		</main>
		</body>
		</html>
	);
}
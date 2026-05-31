import Link from 'next/link';
import styles from './page.module.css';
import { Button } from '@pullapp/ui';
1
export default function HomePage() {
	return (
		<main className={styles.mainContainer}>
			{/* Sekcja Hero */}
			<header className={styles.heroSection}>
				<h1 className={styles.headline}>
					Dziel koszty, <br />
					<span className={styles.highlight}>mnóż znajomości.</span>
				</h1>
				
				<p className={styles.description}>
					PullApp to najszybszy sposób na znalezienie towarzyszy podróży,<br/>
					niezależnie do którego miasta się wybierasz.
				</p>
				
				<div className={styles.ctaGroup}>
					{/* Ścieżka Kierowcy */}
					<Link href="/driver/publish" passHref style={{ textDecoration: "none" }}>
						<Button variant="primary" size="large" className={styles.ctaButton}>
							🚗 &nbsp;Prowadzisz? Opublikuj trasę
						</Button>
					</Link>
					
					{/* Ścieżka Pasażera */}
					<Link href="/passenger/search" passHref style={{ textDecoration: "none" }}>
						<Button variant="secondary" size="large" className={styles.ctaButton}>
							🎒 &nbsp;Dołączasz? Znajdź dopasowanie
						</Button>
					</Link>
				</div>
			</header>
			
			{/* Opcjonalna sekcja zaufania / kroki */}
			<section className={styles.featuresSection}>
				<div className={styles.feature}>
					<h3>📍 Szybkie dopasowanie</h3>
					<p>Nasz algorytm znajdzie pasażerów dokładnie na Twojej trasie.</p>
				</div>
				<div className={styles.feature}>
					<h3>💳 Bezpieczne płatności</h3>
					<p>Rozliczenia odbywają się automatycznie w aplikacji.</p>
					<p>[TODO] Tak naprawdę to jeszcze nie, ale mamy to na liście!</p>
				</div>
			</section>
		</main>
	);
}
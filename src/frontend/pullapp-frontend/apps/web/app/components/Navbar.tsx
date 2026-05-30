'use client';
import Link from 'next/link';
import { Button } from '@pullapp/ui';
import styles from './Navbar.module.css';
import { usePathname } from 'next/navigation';

export function Navbar() {
	const pathname = usePathname();
	if (pathname === '/login' || pathname === '/register') return null;
	
	return (
		<nav className={styles.navbar}>
			<div className={styles.logoContainer}>
				<Link href="/" className={styles.logo}>
					📍 PullApp
				</Link>
			</div>
			
			<div className={styles.navLinks}>
				<Link href="/driver/publish" className={styles.link}>
					Prowadzisz?
				</Link>
				<Link href="/passenger/search" className={styles.link}>
					Dołączasz?
				</Link>
			</div>
			
			<div className={styles.authActions}>
				{/* Mały, dyskretny wariant do logowania */}
				<Link href="/login" passHref style={{ textDecoration: "none" }}>
					<Button variant="secondary" size="medium">
							Zaloguj się
					</Button>
				</Link>
			</div>
		</nav>
	);
}
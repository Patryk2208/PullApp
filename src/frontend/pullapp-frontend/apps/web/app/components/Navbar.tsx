'use client';
import Link from 'next/link';
import { Button } from '@pullapp/ui';
import styles from './Navbar.module.css';
import { usePathname } from 'next/navigation';
import { useAuthStore } from "@pullapp/features";
import React from "react";

export function Navbar() {
	const token = useAuthStore((state) => state.token);
	const logout = useAuthStore((state) => state.logout);
	const isLoggedIn = !!token;
	
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
				<Link href="/trips/publish" className={styles.link}>
					Prowadzisz?
				</Link>
				<Link href="/trips/search" className={styles.link}>
					Dołączasz?
				</Link>
			</div>
			
			<div className={styles.authActions}>
				{isLoggedIn ? (
					// INTERFEJS DLA ZALOGOWANEGO UŻYTKOWNIKA
					<div key="logged-in" className={styles.userMenu}>
						<Link href="/trips/driver" className={styles.link}>
							🚗 Panel kierowcy
						</Link>
						<Link href="/profile" className={styles.link}>
							👤 Mój Profil
						</Link>
						<Button variant="secondary" size="medium" onClick={logout}>
							Wyloguj się
						</Button>
					</div>
				) : (
					// INTERFEJS DLA GOŚCIA
					<Link key="guest" href="/login" passHref style={{ textDecoration: 'none' }}>
						<Button variant="primary" size="medium">
							Zaloguj się
						</Button>
					</Link>
				)}
			</div>
		</nav>
	);
}
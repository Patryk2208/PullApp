'use client';
import { UserRepository } from '@pullapp/api-client';
import { useProfile } from '@pullapp/features';
import styles from './profile.module.css';

const baseUrl = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';
const userRepository = new UserRepository(baseUrl);

export default function ProfilePage() {
	const { profile, isLoading, error } = useProfile(userRepository);
	
	if (isLoading) return <div className={styles.container}>Ładowanie profilu...</div>;
	if (error) return <div className={`${styles.container} ${styles.error}`}>{error}</div>;
	if (!profile) return <div className={styles.container}>Nie znaleziono profilu.</div>;
	
	return (
		<div className={styles.container}>
			<h1>Mój Profil</h1>
			<div className={styles.card}>
				<p><strong>Imię:</strong> {profile.name}</p>
				<p><strong>Nazwisko:</strong> {profile.surname}</p>
				<p><strong>E-mail:</strong> {profile.email}</p>
				<p><strong>ID Użytkownika:</strong> {profile.id}</p>
			</div>
		</div>
	);
}
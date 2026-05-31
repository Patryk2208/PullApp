'use client';
import { UserRepository } from '@pullapp/api-client';
import { useProfile } from '@pullapp/features';
import styles from './profile.module.css';
import React from "react";
import { UserRole } from '@pullapp/domain';

const baseUrl = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';
const userRepository = new UserRepository(baseUrl);

export default function ProfilePage() {
	const { profile, isLoading, error } = useProfile(userRepository);
	
	const formattedBirthDate = React.useMemo(() => {
		if (profile === null || !profile.birthDate) return 'Nie podano';
		const date = new Date(profile.birthDate);
		return new Intl.DateTimeFormat('pl-PL', { day: 'numeric', month: 'long', year: 'numeric' }).format(date); // TODO hardcoded format
	}, [profile?.birthDate]);
	
	if (isLoading) return <div className={styles.container}>Ładowanie profilu...</div>;
	if (error) return <div className={`${styles.container} ${styles.error}`}>{error}</div>;
	if (!profile) return <div className={styles.container}>Nie znaleziono profilu.</div>;
	
	const avatarUrl = profile.profilePicture ??
		`https://ui-avatars.com/api/?name=${encodeURIComponent(profile.name)}+${encodeURIComponent(profile.surname)}&background=0D8ABC&color=fff`;
	
	const ROLE_LABELS = {
		[UserRole.regularUser]: 'zwykły użytkownik',
		[UserRole.admin]: 'administrator',
	};
	
	// profilePictureUri: string | null;
	// birthDate: string;
	// bio: string;
	// role: UserRole;
	return (
		<div className={styles.container}>
			<h1 className={styles.title}>Mój Profil</h1>
			
			<div className={styles.card}>
				<div className={styles.headerSection}>
					<img
						src={avatarUrl}
						alt={`Awatar użytkownika ${profile.name}`}
						className={styles.avatar}
					/>
					<div className={styles.identity}>
						<h2>{profile.name} {profile.surname}</h2>
						<span className={styles.roleBadge}>{ROLE_LABELS[profile.role]}</span>
					</div>
				</div>
				
				<hr className={styles.divider} />
				
				<div className={styles.infoSection}>
					<p><strong>E-mail:</strong> {profile.email}</p>
					<p><strong>Data urodzenia:</strong> {formattedBirthDate}</p>
					<p><strong>ID Użytkownika:</strong> <code className={styles.codeId}>{profile.id}</code></p>
				</div>
				
				{profile.bio && (
					<div className={styles.bioSection}>
						<h3>O mnie</h3>
						<p className={styles.bioText}>{profile.bio}</p>
					</div>
				)}
			</div>
		</div>
	);
}
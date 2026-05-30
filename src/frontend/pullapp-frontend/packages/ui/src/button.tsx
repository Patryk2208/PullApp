import * as React from "react";
import { StyleSheet, Text, Pressable } from "react-native";

// 1. Zdefiniowanie uniwersalnego interfejsu (zgodnego z Web i Native)
export interface ButtonProps {
	children: React.ReactNode;       // Standard Reacta zamiast surowego 'text'
	onClick?: () => void;            // Uproszczony typ zdarzenia
	variant?: "primary" | "secondary";
	size?: "medium" | "large";
	className?: string;              // Przepuszczamy klasę CSS dla weba
}

export function Button({
	                       children,
	                       onClick,
	                       variant = "primary",
	                       size = "medium",
	                       className
                       }: ButtonProps) {
	
	// 2. Tablica stylów (Style Array) - pozwala na nadpisywanie właściwości
	const buttonStyles = [
		styles.baseButton,
		variant === "secondary" ? styles.secondaryButton : styles.primaryButton,
		size === "large" ? styles.largeButton : styles.mediumButton,
	];
	
	return (
		<Pressable
			onPress={onClick}
			// @ts-ignore - className to nie jest oficjalny prop w React Native,
			// ale biblioteka react-native-web (używana przez Next.js) przetworzy go prawidłowo na HTML
			className={className}
			// Wstrzykujemy inline CSS, który zadziała TYLKO na webie i zresetuje 
			// jakiekolwiek domyślne zachowania hiperłączy dla tego przycisku
			// @ts-ignore
			style={({ hovered }) => [
				...buttonStyles,
				// Konwersja dla react-native-web, która zostanie przetłumaczona na styl inline w HTML:
				{ textDecoration: "none", textDecorationLine: "none" }
			]}
		>
			<Text style={[styles.text, variant === "secondary" && styles.textSecondary]}>
				{children}
			</Text>
		</Pressable>
	);
}

// 3. Rozszerzony StyleSheet
const styles = StyleSheet.create({
	baseButton: {
		textAlign: "center",
		borderRadius: 10,
		alignItems: "center",
		justifyContent: "center",
	},
	
	// Warianty kolorystyczne
	primaryButton: {
		backgroundColor: "#3498db", // Kolor główny PullApp
	},
	secondaryButton: {
		backgroundColor: "transparent",
		borderWidth: 2,
		borderColor: "#3498db",
	},
	
	// Warianty rozmiarowe
	mediumButton: {
		paddingVertical: 10,
		paddingHorizontal: 20,
	},
	largeButton: {
		paddingVertical: 16,
		paddingHorizontal: 32,
		width: "100%", // Na stronie głównej duże przyciski wyglądają lepiej
	},
	
	// Style tekstu wewnątrz
	text: {
		color: "white",
		fontSize: 16,
		fontWeight: "bold",
	},
	textSecondary: {
		color: "#3498db", // W wariancie secondary tekst przyjmuje kolor obramowania
	},
});
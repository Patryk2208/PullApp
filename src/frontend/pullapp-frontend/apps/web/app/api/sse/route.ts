export async function GET(request: Request) {
    const authHeader = request.headers.get('Authorization');
    
    const response = await fetch('http://127.0.0.1:8080/sse/notifications', {
        headers: {
            'Authorization': authHeader ?? '',
            'Accept': 'text/event-stream',
        },
    });

    return new Response(response.body, {
        headers: {
            'Content-Type': 'text/event-stream',
            'Cache-Control': 'no-cache',
            'Connection': 'keep-alive',
        },
    });
}
from transport.models import WelcomeMessage


def test_welcome_message_defaults_auth_flags_false():
    msg = WelcomeMessage(serverTimeout=30, keepAliveInterval=15)

    assert msg.authEnabled is False
    assert msg.authTokenRequired is False


def test_welcome_message_allows_auth_flags_true():
    msg = WelcomeMessage(
        serverTimeout=30,
        keepAliveInterval=15,
        authEnabled=True,
        authTokenRequired=True,
    )

    assert msg.authEnabled is True
    assert msg.authTokenRequired is True

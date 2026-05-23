# RabbitMQ com plugin de delayed-message-exchange.
# Necessário para MassTransit UseDelayedRedelivery (segundo nível de retry com delay).
# Sem o plugin, o broker não consegue agendar entregas — só temos retries imediatos.
FROM rabbitmq:3-management-alpine

ARG PLUGIN_VERSION=3.13.0

ADD --chown=rabbitmq:rabbitmq https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v${PLUGIN_VERSION}/rabbitmq_delayed_message_exchange-${PLUGIN_VERSION}.ez \
    /opt/rabbitmq/plugins/

RUN rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange

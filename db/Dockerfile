# Use the official MySQL 8.0 image as the base
FROM mysql:8.0

# Set container OS timezone to GMT+2
ENV TZ=Africa/Johannesburg

# Set the root password (ensure you change this for production environments)
ENV MYSQL_ROOT_PASSWORD=SecretRootPassword123!

# Inform MySQL to use the initialization directory
# Scripts placed here are automatically executed on container startup
COPY install_v1.sql /docker-entrypoint-initdb.d/

# Expose the default MySQL port
EXPOSE 3306

# Start MySQL with GMT+2 default timezone
CMD ["mysqld", "--default-time-zone=+02:00"]